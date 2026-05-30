namespace Prague.Core.Collections;

internal struct HashSlot<T>
{
	public int HashCode;

	public int Next;

	public T Value;
}
