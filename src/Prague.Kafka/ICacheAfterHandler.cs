namespace Prague.Kafka;

public enum UpdateType {
	Filtered = 0,
	Same = 1,
	Add = 2,
	Update = 3,
	Delete = 4
}

public interface ICacheAfterHandler<in TKey, in TValue> {
	ValueTask Handle(UpdateType updateType, TKey key, TValue? newValue, TValue? oldValue);
}