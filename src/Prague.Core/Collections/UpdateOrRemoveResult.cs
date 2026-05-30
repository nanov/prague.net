using System.Runtime.CompilerServices;

namespace Prague.Core.Collections;

public readonly struct UpdateOrRemoveResult<TValue>
{
	public readonly UpdateOrRemoveOperation Operation;

	public readonly TValue? OldValue;

	public readonly TValue? NewValue;

	public static UpdateOrRemoveResult<TValue> NotFound => new UpdateOrRemoveResult<TValue>(UpdateOrRemoveOperation.NotFound, default(TValue), default(TValue));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public UpdateOrRemoveResult(UpdateOrRemoveOperation operation, TValue? oldValue, TValue? newValue)
	{
		Operation = operation;
		OldValue = oldValue;
		NewValue = newValue;
	}
}
