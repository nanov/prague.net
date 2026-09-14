namespace Prague.Kafka.Tests.Filters;

using System.Text;
using Prague.Kafka.Filters;

/// <summary>
///   Pins the two invariants the header gate rests on: which header names are <i>required</i> to be present, and
///   the bound that stops a producer from sizing a stack allocation.
///   <para>
///   Presence is tracked as one bit per required header name. Reaching the end of the header loop without every
///   required bit means — and means only — that a header required by <c>WithHeaderExistsFilter</c> never appeared,
///   which is what lets <c>EvaluateHeaderGate</c> report <see cref="HeaderGate.MissingRequiredHeader" /> as a
///   statement about the message's <i>shape</i> and the consume loop waive it for a delete.
///   </para>
///   <para>
///   These run without a broker. The behaviour is otherwise only reachable through the integration suite, because
///   <c>RawMessage</c> / <c>RawHeaders</c> are byref-like with internal-only constructors.
///   </para>
/// </summary>
[TestFixture]
public class HeaderGateStateTests {
	private static KafkaHeaderFilters Build(params (string Header, KafkaHeaderFilterExecutor Filter)[] filters) {
		if (filters.Length == 0)
			return KafkaHeaderFilters.Create(null);
		var map = new Dictionary<string, List<KafkaHeaderFilterExecutor>>(StringComparer.Ordinal);
		foreach (var (header, filter) in filters) {
			if (!map.TryGetValue(header, out var list))
				map[header] = list = [];
			list.Add(filter);
		}

		return KafkaHeaderFilters.Create(map);
	}

	private static KafkaHeaderFilters BuildRequired(int count) {
		var map = new Dictionary<string, List<KafkaHeaderFilterExecutor>>(StringComparer.Ordinal);
		for (var i = 0; i < count; i++)
			map[$"h{i}"] = [new KafkaHeaderExistsFilter()];
		return KafkaHeaderFilters.Create(map);
	}

	// ---------------------------------------------------------------- required-header mask

	[Test]
	public void RequiredMask_IsZero_WithNoFilters() {
		Assert.That(Build().RequiredMask, Is.Zero);
	}

	[Test]
	public void RequiredMask_IsZero_WithAValueJudgingFilter() {
		// An equals filter judges a header it actually saw; it requires nothing to be present, so a message with
		// no headers at all passes it. Only that asymmetry makes MissingRequiredHeader unambiguous.
		Assert.That(Build(("tenant", new KafkaHeaderEqualsStringFilter("A"))).RequiredMask, Is.Zero);
	}

	[Test]
	public void RequiredMask_HasOneBit_ForAnExistsFilter() {
		Assert.That(Build(("tenant", new KafkaHeaderExistsFilter())).RequiredMask, Is.EqualTo(1UL));
	}

	[Test]
	public void AnExistsFilter_IsSatisfied_WhenItsHeaderAppears() {
		var filters = Build(("tenant", new KafkaHeaderExistsFilter()));
		ulong seen = 0;

		var passed = filters.ShouldProcess(ref seen, "tenant"u8, "A"u8);

		Assert.Multiple(() => {
			Assert.That(passed, Is.True);
			Assert.That(seen, Is.EqualTo(filters.RequiredMask));
		});
	}

	[Test]
	public void UnrelatedHeader_LeavesTheRequirementUnsatisfied() {
		var filters = Build(("tenant", new KafkaHeaderExistsFilter()));
		ulong seen = 0;

		var passed = filters.ShouldProcess(ref seen, "other"u8, "A"u8);

		Assert.Multiple(() => {
			Assert.That(passed, Is.True, "a header nobody filters on is not a rejection");
			Assert.That(seen, Is.Not.EqualTo(filters.RequiredMask), "and it does not satisfy the requirement either");
		});
	}

	/// <summary>
	///   Regression: presence was tracked in a single shared bool, so <c>KafkaHeaderExistsFilter</c> set it without
	///   regard to WHICH name resolved — two required headers composed as OR, and a message carrying only the first
	///   was admitted. The documented rule, and every other filter method, compose with AND.
	/// </summary>
	[Test]
	public void TwoExistsFiltersOnDifferentNames_ComposeWithAnd() {
		var filters = Build(
			("tenant", new KafkaHeaderExistsFilter()),
			("correlation", new KafkaHeaderExistsFilter()));
		ulong onlyOne = 0;
		filters.ShouldProcess(ref onlyOne, "tenant"u8, "A"u8);

		ulong both = 0;
		filters.ShouldProcess(ref both, "tenant"u8, "A"u8);
		filters.ShouldProcess(ref both, "correlation"u8, "c1"u8);

		Assert.Multiple(() => {
			Assert.That(onlyOne, Is.Not.EqualTo(filters.RequiredMask), "one of two required headers must not pass");
			Assert.That(both, Is.EqualTo(filters.RequiredMask));
		});
	}

	[Test]
	public void ARepeatedRequiredHeader_SatisfiesItsRequirementOnce() {
		var filters = Build(("tenant", new KafkaHeaderExistsFilter()));
		ulong seen = 0;

		filters.ShouldProcess(ref seen, "tenant"u8, "A"u8);
		filters.ShouldProcess(ref seen, "tenant"u8, "B"u8);

		Assert.That(seen, Is.EqualTo(filters.RequiredMask), "OR-ing the same bit twice is idempotent");
	}

	[Test]
	public void SixtyFourRequiredHeaders_AreSupported() {
		Assert.That(BuildRequired(KafkaHeaderFilters.MAX_REQUIRED_HEADERS).RequiredMask, Is.EqualTo(ulong.MaxValue));
	}

	[Test]
	public void TheSixtyFifthRequiredHeader_ThrowsAtConstruction() {
		// Deliberately not inside Assert.Multiple: a failing Assert.Throws there returns null and the message
		// assertion would then NRE, hiding the very diagnostic this test exists to report.
		var ex = Assert.Throws<InvalidOperationException>(
			() => BuildRequired(KafkaHeaderFilters.MAX_REQUIRED_HEADERS + 1));
		Assert.That(ex!.Message, Does.Contain("WithHeaderExistsFilter"));
	}

	[Test]
	public void CombiningAnExistsFilterWithAnother_StillRequiresTheHeader() {
		var filters = Build(
			("tenant", new KafkaHeaderExistsFilter()),
			("tenant", new KafkaHeaderEqualsStringFilter("A")));

		Assert.That(filters.RequiredMask, Is.EqualTo(1UL));
	}

	[Test]
	public void CombiningTwoValueJudgingFilters_RequiresNothing() {
		var filters = Build(
			("tenant", new KafkaHeaderEqualsStringFilter("A")),
			("tenant", new KafkaHeaderEqualsStringFilter("B")));

		Assert.That(filters.RequiredMask, Is.Zero);
	}

	[Test]
	public void CombinedFilter_StillRejectsOnTheValueJudgingMember() {
		var filters = Build(
			("tenant", new KafkaHeaderExistsFilter()),
			("tenant", new KafkaHeaderEqualsStringFilter("A")));

		Assert.Multiple(() => {
			ulong wrong = 0;
			Assert.That(filters.ShouldProcess(ref wrong, "tenant"u8, Encoding.UTF8.GetBytes("B")), Is.False);
			ulong right = 0;
			Assert.That(filters.ShouldProcess(ref right, "tenant"u8, Encoding.UTF8.GetBytes("A")), Is.True);
			Assert.That(right, Is.EqualTo(filters.RequiredMask));
		});
	}

	/// <summary>
	///   The filter chain does not catch: a throwing header predicate propagates all the way out. That is what
	///   makes the <c>try</c>/<c>catch</c> in <c>ConsumeRawLoop</c> load-bearing — without it the enclosing handler
	///   rethrows, the consume loop exits, and the <c>finally</c> stops the raw worker of <b>every</b> cache on the
	///   consumer, with the poisoned record replaying from the log on restart.
	/// </summary>
	[Test]
	public void AThrowingHeaderPredicate_PropagatesOutOfTheFilterChain() {
		var filters = Build(("tenant",
			new KafkaHeaderPredicateFilter<int>(static _ => throw new InvalidOperationException("boom"))));

		var ex = Assert.Throws<InvalidOperationException>(() => Evaluate(filters));
		Assert.That(ex!.Message, Is.EqualTo("boom"));
		return;

		// Spans cannot cross a lambda boundary, so the call lives in a local function.
		static void Evaluate(KafkaHeaderFilters filters) {
			ulong seen = 0;
			filters.ShouldProcess(ref seen, "tenant"u8, [0x05]);
		}
	}

	// ---------------------------------------------------------------- header-name length bound

	[Test]
	public void ANameExactlyAsLongAsTheLongestKey_StillResolves() {
		var filters = Build(("tenant", new KafkaHeaderEqualsStringFilter("A")));

		ulong seen = 0;
		Assert.That(filters.ShouldProcess(ref seen, "tenant"u8, "B"u8), Is.False,
			"a six-byte name must still reach its filter and be rejected on the value");
	}

	[Test]
	public void ANameLongerThanTheLongestKey_IsAnsweredWithoutResolving() {
		var filters = Build(("tenant", new KafkaHeaderEqualsStringFilter("A")));

		ulong seen = 0;
		Assert.Multiple(() => {
			Assert.That(filters.ShouldProcess(ref seen, "tenantx"u8, "B"u8), Is.True,
				"it could not have matched any key, so it is not a rejection");
			Assert.That(seen, Is.Zero);
		});
	}

	[Test]
	public void ANonAsciiKey_IsMatchedByItsUtf8Bound() {
		// 'ä' is two UTF-8 bytes but one char: the bound is a BYTE length, so the key must not be measured in chars.
		var filters = Build(("tä", new KafkaHeaderEqualsStringFilter("A")));

		ulong seen = 0;
		Assert.That(filters.ShouldProcess(ref seen, Encoding.UTF8.GetBytes("tä"), "B"u8), Is.False,
			"the three-byte name must still resolve to its filter");
	}

	/// <summary>
	///   A header name is raw wire data, bounded by the broker only by <c>message.max.bytes</c>. Sizing a
	///   <c>stackalloc</c> by it let a producer take the whole host down with an uncatchable stack overflow, and
	///   the record replayed from the log into a crash loop.
	///   <para>
	///   Note for anyone bisecting: against the unfixed code this test does not fail, it <b>kills the test host</b>.
	///   Exclude the <c>StackSafety</c> category if a run dies without a failure report.
	///   </para>
	/// </summary>
	[Test]
	[Category("StackSafety")]
	public void AProducerSizedHeaderName_IsRejectedWithoutTouchingTheStack() {
		var filters = Build(("tenant", new KafkaHeaderEqualsStringFilter("A")));
		var hugeName = new byte[512 * 1024];
		Array.Fill(hugeName, (byte)'x');

		ulong seen = 0;
		Assert.That(filters.ShouldProcess(ref seen, hugeName, "B"u8), Is.True);
	}

	[Test]
	[Category("StackSafety")]
	public void WithNoFiltersConfigured_AProducerSizedNameIsStillSafe() {
		// The common configuration: no header filters at all. The bound is zero, so every name is answered by a
		// compare. The gate's own loop still runs — the producer self-filter must inspect every header.
		var filters = Build();
		var hugeName = new byte[512 * 1024];
		Array.Fill(hugeName, (byte)'x');

		ulong seen = 0;
		Assert.That(filters.ShouldProcess(ref seen, hugeName, "B"u8), Is.True);
	}
}
