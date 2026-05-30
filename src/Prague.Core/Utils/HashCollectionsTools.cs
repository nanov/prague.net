namespace Prague.Core.Utils;

using System.Runtime.CompilerServices;

internal static class HashCollectionsTools {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IEqualityComparer<T> GetEqualityComparer<T>(IEqualityComparer<T>? comparer) {
		comparer ??= EqualityComparer<T>.Default;
		if (typeof(T) == typeof(string) &&
		    NonRandomizedStringEqualityComparer.GetStringComparer(comparer) is { } stringComparer)
			return (IEqualityComparer<T>)stringComparer;
		return comparer;
	}
}