namespace Prague.Generated.Tests.Cache;

using Fixtures.Entities;
using NUnit.Framework;
using Prague.Core;

/// <summary>
///   The rule the docs and agent skills teach for changing a cached row: clone, change the clone, write the clone.
///   These tests pin both halves: the rule works, and the in-place shortcut silently does not.
/// </summary>
[TestFixture]
public class ChangeAResidentRowTests {
	private static (MutableTicketCache Cache, MutableTicket Resident) Seed() {
		var cache = new MutableTicketCache();
		cache.AddOrUpdate(new MutableTicket { Id = 1, State = "open", Tags = ["a"] });
		Assert.That(cache.Cache.TryGet(1, out var resident), Is.True);
		return (cache, resident!);
	}

	private static int CountIn(MutableTicketCache cache, string state) => cache.Query().WithState(state).Count();

	[Test]
	public void CloneChangeWrite_IsAnUpdate_AndTheIndexFollows() {
		var (cache, resident) = Seed();

		var next = resident.Clone();
		next.State = "closed";

		Assert.That(cache.AddOrUpdate(next, out var previous), Is.True);
		Assert.That(previous, Is.SameAs(resident));
		Assert.That(resident.State, Is.EqualTo("open"), "the replaced instance is untouched");
		Assert.That(CountIn(cache, "closed"), Is.EqualTo(1));
		Assert.That(CountIn(cache, "open"), Is.Zero);
	}

	[Test]
	public void ChangeInPlace_ThenWrite_IsSame_AndTheIndexGoesStale() {
		var (cache, resident) = Seed();

		resident.State = "closed";

		Assert.That(cache.AddOrUpdate(resident, out _), Is.False, "the instance equals itself");
		Assert.That(CountIn(cache, "closed"), Is.Zero, "the index never saw the change");
		Assert.That(CountIn(cache, "open"), Is.EqualTo(1), "and still files the row under its old value");
	}

	[Test]
	public void ChangingAWrittenInstance_LeavesTheIndexStale() {
		var (cache, resident) = Seed();
		var next = resident.Clone();
		next.State = "closed";
		cache.AddOrUpdate(next);

		next.State = "open";   // next is now the resident instance

		Assert.That(CountIn(cache, "closed"), Is.EqualTo(1));
		Assert.That(CountIn(cache, "open"), Is.Zero);
	}

	[Test]
	public void Clone_IsDeep() {
		var (cache, resident) = Seed();

		var next = resident.Clone();
		next.Tags.Add("b");

		Assert.That(resident.Tags, Is.EqualTo(new[] { "a" }));
		Assert.That(cache.AddOrUpdate(next, out _), Is.True);
	}

	[Test]
	public void ClonedQueryRows_CanBeChangedAndWrittenBack() {
		var (cache, resident) = Seed();

		using var rows = cache.Query().WithState("open").ExecutePooledCloned();
		Assert.That(rows[0], Is.Not.SameAs(resident));
		rows[0].State = "closed";

		Assert.That(cache.AddOrUpdate(rows[0], out _), Is.True);
		Assert.That(CountIn(cache, "closed"), Is.EqualTo(1));
	}
}
