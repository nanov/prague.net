namespace Prague.Core.Utils;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

internal readonly unsafe struct RefHolder<T> where T : struct, allows ref struct {
	private readonly void* _ptr;

	public RefHolder() {
		_ptr = null;
	}

	public RefHolder(ref T value) {
		_ptr = Unsafe.AsPointer(ref value);
	}

	[UnscopedRef]
	public readonly ref T Value
		=> ref Unsafe.AsRef<T>(_ptr);
}