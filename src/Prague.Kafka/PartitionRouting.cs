namespace Prague.Kafka;

using System.ComponentModel;
using System.Runtime.CompilerServices;
using Core;

/// <summary>
///   Chooses the partition a cache write is produced to. Called by the generated <c>…AndProduce</c> methods before
///   the local cache is touched, so a rejected request changes nothing.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class PartitionRouting {
	/// <summary>
	///   Resolves the target partition of a write.
	///   <list type="bullet">
	///     <item>No partition requested: the record's stored partition, or <see cref="PersistenceInfo.UnknownPartition" />
	///     (the producer's partitioner chooses) when the key is not resident or was written here without one.</item>
	///     <item>A partition requested: it must be within the topic's partition count when that is known, and must
	///     equal the stored partition when there is one -- a key's partition is immutable.</item>
	///   </list>
	/// </summary>
	/// <param name="requested">The caller's partition, or <see cref="PersistenceInfo.UnknownPartition" /> for none.</param>
	/// <param name="stored">The resident record's partition, or <see cref="PersistenceInfo.UnknownPartition" />.</param>
	/// <param name="partitionCount">The topic's partition count; 0 when unknown.</param>
	/// <param name="topic">The topic, for the exception message.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Resolve(int requested, int stored, int partitionCount, string topic) {
		if (requested == PersistenceInfo.UnknownPartition)
			return stored;
		if (requested < 0 || (partitionCount > 0 && requested >= partitionCount))
			ThrowOutOfRange(requested, partitionCount, topic);
		if (stored != PersistenceInfo.UnknownPartition && stored != requested)
			ThrowImmutable(requested, stored, topic);
		return requested;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowOutOfRange(int requested, int partitionCount, string topic) =>
		throw new ArgumentOutOfRangeException(nameof(requested), requested,
			$"Partition {requested} does not exist on topic '{topic}', which has {partitionCount} partition(s).");

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowImmutable(int requested, int stored, string topic) =>
		throw new InvalidOperationException(
			$"The key is stored on partition {stored} of topic '{topic}'; it cannot be written to partition {requested}. "
			+ "A key's partition is immutable: writing it elsewhere would leave the old value live on its partition.");
}
