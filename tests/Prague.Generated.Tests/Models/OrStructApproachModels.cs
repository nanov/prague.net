namespace Prague.Generated.Tests.Models;

using global::TestModels;

public struct StatusAndTimeStamp : IEquatable<StatusAndTimeStamp>, IComparable<StatusAndTimeStamp> {
	public readonly ReadingStatus ReadingStatus;
	public readonly DateTimeOffset ActiveTimestamp;
	public readonly DateTimeOffset DraftTimestamp;


	public StatusAndTimeStamp(DateTimeOffset draftTimestamp, DateTimeOffset activeTimestamp) {
		ActiveTimestamp = activeTimestamp;
		DraftTimestamp = draftTimestamp;
	}

	public StatusAndTimeStamp(ReadingStatus status, DateTimeOffset draftTimestamp, DateTimeOffset activeTimestamp) {
		ReadingStatus = status;
		ActiveTimestamp = activeTimestamp;
		DraftTimestamp = draftTimestamp;
	}

	public int CompareTo(StatusAndTimeStamp other) {
		return ReadingStatus switch {
			ReadingStatus.Active => ActiveTimestamp.CompareTo(other.ActiveTimestamp),
			ReadingStatus.Draft => DraftTimestamp.CompareTo(other.DraftTimestamp),
			_ => 1
		};
	}

	public bool Equals(StatusAndTimeStamp other) {
		return ReadingStatus == other.ReadingStatus
		       && ActiveTimestamp.Equals(other.ActiveTimestamp)
		       && DraftTimestamp.Equals(other.DraftTimestamp);
	}

	public override bool Equals(object? obj) {
		return obj is StatusAndTimeStamp other
		       && Equals(other);
	}

	public override int GetHashCode() {
		return HashCode.Combine((int)ReadingStatus, ActiveTimestamp, DraftTimestamp);
	}
}
