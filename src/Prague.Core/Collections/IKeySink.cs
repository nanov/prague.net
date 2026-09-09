namespace Prague.Core.Collections;

/// <summary>A key receiver an index copies its keys into, so the copy loop runs once per index type and the receiver (a stack buffer, a set) stays generic.</summary>
internal interface IKeySink<in TKey> {
	void Add(TKey key);
}
