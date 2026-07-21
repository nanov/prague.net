namespace Prague.Core.Utils;

	using System.Runtime.CompilerServices;

/// <summary>
///   The Fibonacci (Knuth multiplicative) mix <c>DefaultKeyComparer</c> applies to value-type
///   hashes, plus its exact inverse. Multiplication by an odd constant is a bijection on uint,
///   so <c>Unmix(Mix(h)) == h</c> for every h. <c>PooledBTree</c> uses <see cref="Unmix"/> to
///   recover the raw <c>T.GetHashCode()</c> tiebreak hash from a store-computed key hash with
///   one multiply instead of re-hashing the key (2026-07-20 single-hash spec).
/// </summary>
internal static class HashMixing {
	internal const uint Fibonacci = 2654435769U; // floor(2^32 / golden ratio); odd → invertible
	internal const uint FibonacciInverse = 340573321U; // Fibonacci * FibonacciInverse ≡ 1 (mod 2^32)

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int Mix(int rawHash) => (int)((uint)rawHash * Fibonacci);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static int Unmix(int mixedHash) => (int)((uint)mixedHash * FibonacciInverse);
}
