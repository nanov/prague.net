namespace Prague.Core.Utils;

using System.Numerics;

internal static class StringTools {
	internal static unsafe int GetNonRandomizedHashCode(string s) {
		var lenght = s.Length;
		fixed (char* src = s) {
			uint hash1 = (5381 << 16) + 5381;
			var hash2 = hash1;

			var ptr = (uint*)src;
			var length = lenght;

			while (length > 2) {
				length -= 4;
				hash1 = (BitOperations.RotateLeft(hash1, 5) + hash1) ^ ptr[0];
				hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[1];
				ptr += 2;
			}

			if (length > 0) hash2 = (BitOperations.RotateLeft(hash2, 5) + hash2) ^ ptr[0];

			return (int)(hash1 + hash2 * 1566083941);
		}
	}

	internal static class RandomizedStringEqualityComparer {
		internal static IEqualityComparer<string> Create(bool ignoreCase) {
			return !ignoreCase
				? StringComparer.Ordinal
				: StringComparer.OrdinalIgnoreCase;
		}
	}
}

public class NonRandomizedStringEqualityComparer : IEqualityComparer<string?> {
	private static readonly NonRandomizedStringEqualityComparer WrappedAroundDefaultComparer =
		new OrdinalComparer(EqualityComparer<string?>.Default);

	private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinal =
		new OrdinalComparer(StringComparer.Ordinal);

	private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinalIgnoreCase =
		new OrdinalIgnoreCaseComparer(StringComparer.OrdinalIgnoreCase);

	private readonly IEqualityComparer<string?> _underlyingComparer;

	private NonRandomizedStringEqualityComparer(IEqualityComparer<string?> underlyingComparer) {
		_underlyingComparer = underlyingComparer;
	}

	public virtual bool Equals(string? x, string? y) {
		return string.Equals(x, y);
	}

	public virtual int GetHashCode(string? obj) {
		return obj is null ? 0 : StringTools.GetNonRandomizedHashCode(obj);
		// :e?.GetNonRandomizedHashCode() ?? 0;
	}

	internal virtual IEqualityComparer<string> GetRandomizedEqualityComparer() {
		return _underlyingComparer; //  RandomizedStringEqualityComparer.Create(_underlyingComparer, ignoreCase: false);
	}

	public virtual IEqualityComparer<string?> GetUnderlyingEqualityComparer() {
		return _underlyingComparer;
	}

	public static IEqualityComparer<string>? GetStringComparer(object comparer) {
		if (ReferenceEquals(comparer, EqualityComparer<string>.Default))
			return WrappedAroundDefaultComparer;

		if (ReferenceEquals(comparer, StringComparer.Ordinal))
			return WrappedAroundStringComparerOrdinal;

		if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
			return WrappedAroundStringComparerOrdinalIgnoreCase;

		return null;
	}

	private sealed class OrdinalComparer : NonRandomizedStringEqualityComparer {
		internal OrdinalComparer(IEqualityComparer<string?> wrappedComparer)
			: base(wrappedComparer) {
		}

		public override bool Equals(string? x, string? y) {
			return string.Equals(x, y);
		}

		public override int GetHashCode(string? obj) {
			return obj is null ? 0 : StringTools.GetNonRandomizedHashCode(obj);
		}
	}

	private sealed class OrdinalIgnoreCaseComparer : NonRandomizedStringEqualityComparer {
		internal OrdinalIgnoreCaseComparer(IEqualityComparer<string?> wrappedComparer)
			: base(wrappedComparer) {
		}

		public override bool Equals(string? x, string? y) {
			return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
		}

		public override int GetHashCode(string? obj) {
			if (obj is null)
				return 0;
			return string.GetHashCode(obj, StringComparison.OrdinalIgnoreCase);
		}

		internal override IEqualityComparer<string?> GetRandomizedEqualityComparer() {
			return StringComparer.OrdinalIgnoreCase;
		}
	}
}