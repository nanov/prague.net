namespace Prague.Core.Tests.Collections;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class QuickLookupCacheTests {
	[Test]
	public void GetOrAdd_FirstCall_InvokesFactoryExactlyOnce() {
		var cache = new QuickLookupCache<string, string>();
		var count = 0;

		var value = cache.GetOrAdd("k", (_, _) => {
			count++;
			return "v";
		}, default(byte));

		Assert.That(count, Is.EqualTo(1));
		Assert.That(value, Is.EqualTo("v"));
	}

	[Test]
	public void GetOrAdd_RepeatedReads_ReturnSameInstance() {
		var cache = new QuickLookupCache<string, object>();
		var instance = new object();

		var first = cache.GetOrAdd("k", (_, state) => state, instance);
		var second = cache.GetOrAdd("k", (_, _) => new object(), default(byte));

		Assert.That(first, Is.SameAs(instance));
		Assert.That(second, Is.SameAs(instance));
	}

	[Test]
	public void GetOrAddRef_RepeatedCalls_ReturnReferenceEqualValuesAndStableRef() {
		var cache = new QuickLookupCache<string, object>();
		var instance = new object();

		ref readonly var first = ref cache.GetOrAddRef("k", (_, state) => state, instance);
		var firstSnapshot = first;
		ref readonly var second = ref cache.GetOrAddRef("k", (_, _) => new object(), default(byte));
		var secondSnapshot = second;

		Assert.That(firstSnapshot, Is.SameAs(instance));
		Assert.That(secondSnapshot, Is.SameAs(instance));

		// The frozen dictionary backing store is stable across reads (no mutation between the calls),
		// so the ref returned by GetValueRefOrNullRef must point to the same slot.
		ref readonly var slotA = ref cache.Snapshot.GetValueRefOrNullRef("k");
		ref readonly var slotB = ref cache.Snapshot.GetValueRefOrNullRef("k");
		Assert.That(Unsafe.AreSame(in slotA, in slotB), Is.True);
	}

	[Test]
	public async Task GetOrAdd_ConcurrentSameKey_AllCallersReceiveSameInstance() {
		const int n = 32;
		var cache = new QuickLookupCache<string, object>();
		var factoryCount = 0;
		var results = new ConcurrentBag<object>();
		using var gate = new ManualResetEventSlim(false);

		var tasks = new Task[n];
		for (var i = 0; i < n; i++) {
			tasks[i] = Task.Run(() => {
				gate.Wait();
				var value = cache.GetOrAdd("same", (_, _) => {
					Interlocked.Increment(ref factoryCount);
					return new object();
				}, default(byte));
				results.Add(value);
			});
		}

		gate.Set();
		await Task.WhenAll(tasks);

		Assert.That(factoryCount, Is.GreaterThanOrEqualTo(1));
		Assert.That(results, Has.Count.EqualTo(n));
		var first = cache.GetOrAdd("same", (_, _) => new object(), default(byte));
		foreach (var r in results)
			Assert.That(r, Is.SameAs(first));
		Assert.That(cache.Snapshot.Count, Is.EqualTo(1));
	}

	[Test]
	public async Task GetOrAdd_ConcurrentDistinctKeys_AllKeysLanded() {
		const int n = 32;
		var cache = new QuickLookupCache<int, string>();
		using var gate = new ManualResetEventSlim(false);

		var tasks = new Task[n];
		for (var i = 0; i < n; i++) {
			var idx = i;
			tasks[i] = Task.Run(() => {
				gate.Wait();
				cache.GetOrAdd(idx, (k, _) => "v" + k, default(byte));
			});
		}

		gate.Set();
		await Task.WhenAll(tasks);

		Assert.That(cache.Snapshot.Count, Is.EqualTo(n));
		for (var i = 0; i < n; i++)
			Assert.That(cache.Snapshot[i], Is.EqualTo("v" + i));
	}

	[Test]
	public void Snapshot_NoMutation_IsReferenceEqual_AndChangesAfterAdd() {
		var cache = new QuickLookupCache<string, string>();
		cache.GetOrAdd("a", (_, _) => "1", default(byte));

		var snapA = cache.Snapshot;
		var snapB = cache.Snapshot;
		Assert.That(snapA, Is.SameAs(snapB));

		cache.GetOrAdd("b", (_, _) => "2", default(byte));
		var snapC = cache.Snapshot;
		Assert.That(snapC, Is.Not.SameAs(snapA));
		Assert.That(snapA.Count, Is.EqualTo(1));
		Assert.That(snapC.Count, Is.EqualTo(2));
	}
}
