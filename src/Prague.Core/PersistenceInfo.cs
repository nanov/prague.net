namespace Prague.Core;

/// <summary>
/// Metadata about the persistence origin of a cache item.
/// Contains the timestamp and offset from the source stream (e.g., Kafka).
/// </summary>
public readonly struct PersistenceInfo
{
    /// <summary>
    /// Unix timestamp in milliseconds when the item was produced.
    /// </summary>
    public readonly long Timestamp;

    /// <summary>
    /// Offset of the item in the source stream partition.
    /// </summary>
    public readonly long Offset;

    public PersistenceInfo(long timestamp, long offset)
    {
        Timestamp = timestamp;
        Offset = offset;
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
    void SetPragueMetadata(long timestamp, long offset);
}
