namespace Prague.Kafka;

public enum UpdateType {
	/// <summary>
	/// A message was dropped by a header, key or value filter. Live phase only — rejections during the initial
	/// load are silent.
	/// <para>
	/// <b>Carries no key and no values.</b> <c>key</c> is <c>default</c> — <c>0</c> for an <c>int</c> key,
	/// <c>null</c> for a reference-typed one — and both values are <c>null</c>. A handler learns only that a
	/// message was dropped, never which one. Do not read <c>key</c> on this update type.
	/// </para>
	/// </summary>
	Filtered = 0,

	/// <summary>The message carried a value equal to the one already cached; the cache was not changed.</summary>
	Same = 1,

	/// <summary>The key was not in the cache and has been added. <c>oldValue</c> is <c>null</c>.</summary>
	Add = 2,

	/// <summary>The key was in the cache and its value changed. Both <c>newValue</c> and <c>oldValue</c> are set.</summary>
	Update = 3,

	/// <summary>
	/// The key was removed — by a tombstone, or by a filter registered with <c>treatAsDelete</c>. <c>newValue</c> is
	/// <c>null</c>; <c>oldValue</c> is the removed value. Fires only when the key was actually resident: a delete
	/// for a key this cache never held is a silent no-op.
	/// </summary>
	Delete = 4
}

public interface ICacheAfterHandler<in TKey, in TValue> {
	/// <summary>
	///   Called on the live path after the cache has been updated. See <see cref="UpdateType" /> for which
	///   arguments are populated for each kind — in particular <see cref="UpdateType.Filtered" /> carries neither
	///   the key nor a value.
	/// </summary>
	ValueTask Handle(UpdateType updateType, TKey key, TValue? newValue, TValue? oldValue);
}
