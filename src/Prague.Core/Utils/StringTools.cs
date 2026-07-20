namespace Prague.Core.Utils;

using System.Numerics;

internal static class StringTools {
	// DJB2-style dual-lane hash — culture-independent, stable within a process run, and cheap.
	// Used by PooledBTree as the composite-ordering tiebreak for duplicate keys; NOT a bucket
	// hash (hash-table diffusion is Marvin32 via string.GetHashCode in DefaultKeyComparer).
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
}
