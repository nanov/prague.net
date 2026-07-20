namespace Prague.Core.Tests.Cache;

using System.Reflection;
using Prague.Core.Collections;
using NUnit.Framework;

/// <summary>
/// Pins the lock-lifecycle contract of <see cref="ConcurrentCacheStore{TKey,TValue}"/> under
/// lock-array growth (default ctor, <c>growLockArray</c>). The safety of releasing through a
/// re-read <c>_tables.Locks</c> rests on one invariant: growing the lock array copies the old
/// lock object references forward, so element identity is preserved for every previously
/// acquired index. These tests prove that invariant directly and exercise full-lock sweeps
/// racing table/lock growth — a violation would surface as SynchronizationLockException,
/// IndexOutOfRangeException, or a deadlocked stripe.
/// </summary>
[TestFixture]
public class ConcurrentCacheStoreLockingTests {
	private static object[] GetLocksArray<TKey, TValue>(ConcurrentCacheStore<TKey, TValue> cache)
		where TKey : notnull {
		var tablesField = typeof(ConcurrentCacheStore<TKey, TValue>)
			.GetField("_tables", BindingFlags.NonPublic | BindingFlags.Instance)!;
		var tables = tablesField.GetValue(cache)!;
		var locksField = tables.GetType().GetField("Locks", BindingFlags.NonPublic | BindingFlags.Instance)!;
		return (object[])locksField.GetValue(tables)!;
	}

	[Test]
	public void LockArrayGrowth_PreservesLockObjectIdentity() {
		// Default ctor → growLockArray: resizes double the lock array. ReleaseLocks exits
		// through a fresh _tables.Locks read, which is only safe if every index it entered
		// still holds the same lock object after a swap.
		var cache = new ConcurrentCacheStore<int, int>();
		var locksBefore = GetLocksArray(cache);

		for (var i = 0; i < 100_000; ++i)
			cache.AddOrUpdate(i, i, static (_, _, _) => true);

		var locksAfter = GetLocksArray(cache);
		Assert.That(locksAfter.Length, Is.GreaterThan(locksBefore.Length),
			"insert volume must force lock-array growth for this test to prove anything");
		for (var i = 0; i < locksBefore.Length; ++i)
			Assert.That(ReferenceEquals(locksBefore[i], locksAfter[i]), Is.True,
				$"lock object at index {i} must be copied forward across growth");
	}

	[Test]
	[CancelAfter(60_000)]
	public void FullLockSweeps_RacingTableAndLockGrowth_NoExceptionAndConsistentFinalState() {
		// Writers force repeated resizes (and lock-array doublings) while sweep threads run
		// the acquire-all-locks operations. If release ever targeted a lock it did not enter,
		// Monitor.Exit would throw SynchronizationLockException here.
		var cache = new ConcurrentCacheStore<int, int>();
		const int writers = 2;
		const int perWriter = 50_000;
		var failures = new List<Exception>();
		var writersDone = 0;

		var writerThreads = new Thread[writers];
		for (var w = 0; w < writers; ++w) {
			var offset = w * perWriter;
			writerThreads[w] = new Thread(() => {
				try {
					for (var i = 0; i < perWriter; ++i)
						cache.AddOrUpdate(offset + i, i, static (_, _, _) => true);
				} catch (Exception ex) {
					lock (failures) {
						failures.Add(ex);
					}
				} finally {
					Interlocked.Increment(ref writersDone);
				}
			});
		}

		var sweepThreads = new Thread[3];
		for (var s = 0; s < sweepThreads.Length; ++s) {
			sweepThreads[s] = new Thread(() => {
				try {
					while (Volatile.Read(ref writersDone) < writers) {
						_ = cache.Count;
						_ = cache.GetValues();
						_ = cache.CountValues(static v => v % 2 == 0);
						_ = cache.GetKeyValues();
					}
				} catch (Exception ex) {
					lock (failures) {
						failures.Add(ex);
					}
				}
			});
		}

		foreach (var thread in writerThreads)
			thread.Start();
		foreach (var thread in sweepThreads)
			thread.Start();
		foreach (var thread in writerThreads)
			thread.Join();
		foreach (var thread in sweepThreads)
			thread.Join();

		Assert.That(failures, Is.Empty,
			() => "concurrent sweep/growth raised: " + string.Join("; ", failures));
		Assert.That(cache.Count, Is.EqualTo(writers * perWriter));
		Assert.That(cache.CountValues(static _ => true), Is.EqualTo(writers * perWriter));
	}
}
