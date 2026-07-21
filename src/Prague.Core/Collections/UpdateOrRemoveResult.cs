using System.Runtime.CompilerServices;

namespace Prague.Core.Collections;

public readonly struct UpdateOrRemoveResult<TValue>
{
	public readonly UpdateOrRemoveOperation Operation;

	public readonly TValue? OldValue;

	public readonly TValue? NewValue;

	public readonly int KeyHash;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static UpdateOrRemoveResult<TValue> NotFound(int keyHash) => new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.NotFound, default(TValue), default(TValue), keyHash);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateOrRemoveResult(UpdateOrRemoveOperation operation, TValue? oldValue, TValue? newValue, int keyHash)
	{
		Operation = operation;
		OldValue = oldValue;
		NewValue = newValue;
		KeyHash = keyHash;
	}
}
