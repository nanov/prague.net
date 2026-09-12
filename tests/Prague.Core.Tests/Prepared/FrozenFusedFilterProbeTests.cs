namespace Prague.Core.Tests.Prepared;

using System.Reflection;
using Prague.Core;
using Prague.Core.TypeSystem;
using static PreparedQueryJoinDifferentialTests;

// The safety net under the fused filtered JoinOne (step 3c). FusedJoinFilterProbe decides that a join's
// filter callback is a pure value predicate by running it against a paired core in PROBE mode, where every
// narrowing entry point counts itself and returns instead of touching the candidates. If one entry point
// were left unguarded, a callback that narrows by index would report "predicate only" and the fused fill
// would emit rights the paired read would have filtered away — silently wrong results. The guard's
// completeness is therefore not left to inspection.
//
// Coverage is proved from the IL rather than by calling the members: several take a ReadOnlySpan, which
// reflection cannot pass at all. For every ICandidatesFilterer<TKey, TValue> member the walk below asks
// whether the implementing method reads _probe, directly or through another method of the same struct — so
// a delegating overload is covered by its target. WhereInternal is the one member that must NOT be
// guarded: capturing its predicate is the point. A member added later fails here the moment it exists.
//
// It has already earned its keep: the two List<TIndexKey> overloads delegated to their span twin through
// `((ICandidatesFilterer<,>)this).UseIndexInternal(...)`, and that cast boxes the struct — so the copy
// bumped the counter and the copy was thrown away. They carry their own guard now.
[TestFixture]
public class FrozenFusedFilterProbeTests {
	private const string Captured = "WhereInternal";
	private const string Guard = "_probe";

	[Test]
	public void EveryNarrowingEntryPointOnThePairedCoreIsProbeGuarded() {
		var core = typeof(PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>).GetGenericTypeDefinition();
		var args = core.GetGenericArguments();
		var filterer = typeof(ICandidatesFilterer<,>).MakeGenericType(args[1], args[2]);
		var map = core.GetInterfaceMap(filterer);
		Assert.That(map.InterfaceMethods, Has.Length.GreaterThan(20), "the interface shrank — re-check what narrowing paths exist");

		var unguarded = new List<string>();
		var captured = 0;
		for (var i = 0; i < map.InterfaceMethods.Length; i++) {
			if (map.InterfaceMethods[i].Name == Captured) {
				captured++;
				Assert.That(ReadsGuard(map.TargetMethods[i], core, []), Is.False, "WhereInternal must not be guarded — the probe captures its predicate");
				continue;
			}

			if (!ReadsGuard(map.TargetMethods[i], core, []))
				unguarded.Add(Describe(map.InterfaceMethods[i]));
		}

		Assert.That(captured, Is.EqualTo(1), "exactly one member is captured rather than refused");
		Assert.That(unguarded, Is.Empty, "not probe-guarded — a filter callback using these would fuse as if it were a pure predicate");
	}

	// OrWith is not an ICandidatesFilterer member, so the walk above cannot reach it. Invoked for real here:
	// it must count itself AND leave the branch lambdas unrun.
	[Test]
	public void OrWithIsProbeGuardedAndRunsNoBranch() {
		var core = typeof(PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>);
		var narrowings = core.GetField("_narrowings", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var orWith = core.GetMethod("OrWith")!;
		object boxed = new PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>(new InMemoryDataCache<int, PqCustomer>(), probe: true);
		var closed = orWith.MakeGenericMethod(orWith.GetGenericArguments()
			.Select(a => a.Name switch {
				"TCache" => typeof(InMemoryDataCache<int, PqCustomer>),
				"TResolverChain" => typeof(Resolvers<BaseResolver<int, PqCustomer>>),
				"TResult" => typeof(PqCustomer),
				_ => typeof(ThrowingOrBranch),
			}).ToArray());
		closed.Invoke(boxed, Arguments(closed));
		Assert.That((int)narrowings.GetValue(boxed)!, Is.GreaterThan(0), "OrWith did not count itself: it is not probe-guarded");
	}

	/// <summary>A branch that throws if the probe ever runs it.</summary>
	private readonly struct ThrowingOrBranch : IOrBranch<CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<int, PqCustomer>>,
		PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>, int, PqCustomer, Resolvers<BaseResolver<int, PqCustomer>>, PqCustomer>> {
		public CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<int, PqCustomer>>,
			PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>, int, PqCustomer, Resolvers<BaseResolver<int, PqCustomer>>, PqCustomer> Apply(
			CacheQueryBuilderCombined<NarrowOnlyQuery<InMemoryDataCache<int, PqCustomer>>,
				PairedCacheQueryBuilderCoreCombined<int, int, PqCustomer>, int, PqCustomer, Resolvers<BaseResolver<int, PqCustomer>>, PqCustomer> q)
			=> throw new InvalidOperationException("a probe must not run an Or branch");
	}

	// ── The IL walk ──────────────────────────────────────────────────────────────

	private const byte Ldfld = 0x7B;
	private const byte Ldflda = 0x7C;
	private const byte Call = 0x28;
	private const byte Callvirt = 0x6F;

	/// <summary>Does <paramref name="method" /> read the probe flag, itself or through another method of the same struct?</summary>
	private static bool ReadsGuard(MethodInfo method, Type core, HashSet<MethodInfo> seen) {
		if (!seen.Add(method))
			return false;
		var il = method.GetMethodBody()?.GetILAsByteArray();
		if (il is null)
			return false;
		var module = method.Module;
		var typeArgs = method.DeclaringType!.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
		var methodArgs = method.IsGenericMethodDefinition ? method.GetGenericArguments() : null;
		var callees = new List<MethodInfo>();
		// A linear scan over the body: every candidate is confirmed by resolving its token, and a byte of
		// operand data that happens to look like an opcode would have to resolve to _probe or to a method of
		// this same struct to matter. Cheap, and wrong only in the safe direction.
		for (var i = 0; i + 4 < il.Length; i++) {
			var op = il[i];
			if (op is not (Ldfld or Ldflda or Call or Callvirt))
				continue;
			var token = BitConverter.ToInt32(il, i + 1);
			try {
				if (op is Ldfld or Ldflda) {
					if (module.ResolveField(token, typeArgs, methodArgs)?.Name == Guard)
						return true;
				} else if (module.ResolveMethod(token, typeArgs, methodArgs) is MethodInfo callee
				           && callee.DeclaringType is { IsGenericType: true } declaring
				           && declaring.GetGenericTypeDefinition() == core) {
					callees.Add(callee);
				}
			} catch (ArgumentException) {
				// Not a metadata token — this byte was operand data, not an opcode.
			}
		}

		foreach (var callee in callees)
			if (ReadsGuard(GenericDefinitionOf(callee), core, seen))
				return true;
		return false;
	}

	private static MethodInfo GenericDefinitionOf(MethodInfo m)
		=> m.IsGenericMethod && !m.IsGenericMethodDefinition ? m.GetGenericMethodDefinition() : m;

	private static object?[] Arguments(MethodInfo method) {
		var parameters = method.GetParameters();
		var values = new object?[parameters.Length];
		for (var i = 0; i < parameters.Length; i++) {
			var t = parameters[i].ParameterType;
			if (t.IsByRef)
				t = t.GetElementType()!;
			values[i] = t.IsValueType ? Activator.CreateInstance(t) : null;
		}

		return values;
	}

	private static string Describe(MethodInfo member)
		=> member.Name + "(" + string.Join(", ", member.GetParameters().Select(p => p.ParameterType.Name)) + ")";
}
