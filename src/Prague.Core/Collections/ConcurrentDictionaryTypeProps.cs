namespace Prague.Core.Collections;

internal static class ConcurrentDictionaryTypeProps<T> {
	internal static readonly bool _isWriteAtomic = IsWriteAtomicPrivate();

	private static bool IsWriteAtomicPrivate() {
		if (!typeof(T).IsValueType || typeof(T) == typeof(nint) || typeof(T) == typeof(nuint))
			return true;

		switch (Type.GetTypeCode(typeof(T))) {
			case TypeCode.Boolean:
			case TypeCode.Char:
			case TypeCode.SByte:
			case TypeCode.Byte:
			case TypeCode.Int16:
			case TypeCode.UInt16:
			case TypeCode.Int32:
			case TypeCode.UInt32:
			case TypeCode.Single:
				return true;
			case TypeCode.Int64:
			case TypeCode.UInt64:
			case TypeCode.Double:
				return IntPtr.Size == 8;
			default:
				return false;
		}
	}
}