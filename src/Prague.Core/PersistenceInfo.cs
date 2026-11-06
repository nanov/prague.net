namespace Prague.Core;

/// <summary>
/// Metadata about the persistence origin of a cache item.
/// Contains the timestamp, offset and partition from the source stream (e.g., Kafka).
/// </summary>
public readonly struct PersistenceInfo
{
    /// <summary>The <see cref="Partition" /> of an item whose source partition is not known.</summary>
    public const int UnknownPartition = -1;

    /// <summary>
    /// Unix timestamp in milliseconds when the item was produced.
    /// </summary>
    public readonly long Timestamp;

    /// <summary>
    /// Offset of the item in the source stream partition.
    /// </summary>
    public readonly long Offset;

    // Stored as partition + 1 so that default(PersistenceInfo) -- an item written locally and never consumed --
    // reads as UnknownPartition rather than as partition 0.
    private readonly int _partitionPlusOne;

    /// <summary>
    /// Source partition of the item, or <see cref="UnknownPartition" /> when it is not known: an item written in
    /// this process and not consumed back, whose partition the producer's partitioner chose.
    /// </summary>
    public int Partition => _partitionPlusOne - 1;

    /// <summary>
    /// Metadata on partition 0: the pre-partition form, from when every cache topic had a single partition.
    /// </summary>
    public PersistenceInfo(long timestamp, long offset) : this(timestamp, offset, 0)
    {
    }

    public PersistenceInfo(long timestamp, long offset, int partition)
    {
        Timestamp = timestamp;
        Offset = offset;
        _partitionPlusOne = partition + 1;
    }
}

/// <summary>
/// Interface for cache items that can receive persistence metadata.
/// Implemented by generated cache item partial classes.
/// </summary>
public interface IPragueMetadataSettable
{
    /// <summary>
    /// Gets the persistence metadata for this cache item.
    /// </summary>
    PersistenceInfo __PragueMetadata__ { get; }

    /// <summary>
    /// Sets the persistence metadata for this cache item.
    /// </summary>
    void SetPragueMetadata(long timestamp, long offset, int partition);
}
