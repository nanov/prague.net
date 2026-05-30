namespace Prague.Core.Utils;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static class QueryResultMarshal {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<T> Clone<T>(this QueryResults<T> source) where T : ICacheClonable<T> {
		if (typeof(T).IsValueType)
			CloneInPlaceValueType(source.AsSpan());
		else
			CloneInPlaceRefType(source.AsSpan());
		return source;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static QueryResults<T> CloneInPlace<T>(this QueryResults<T> source) where T : ICacheClonable<T> {
		if (typeof(T).IsValueType)
			CloneInPlaceValueType(source.AsSpan());
		else
			CloneInPlaceRefType(source.AsSpan());
		return source;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneInPlace<T>(Span<T> source) where T : ICacheClonable<T> {
		if (typeof(T).IsValueType)
			CloneInPlaceValueType(source);
		else
			CloneInPlaceRefType(source);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CloneInPlace<T, TCloner>(Span<T> source, TCloner cloner) where TCloner : ICloner<T> {
		if (typeof(T).IsValueType)
			CloneInPlaceValueType(source, cloner);
		else
			CloneInPlaceRefType(source, cloner);
	}

	public static void Map<T, TResult>(Span<T> source, Span<TResult> destination, Func<T, TResult> mapper) {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);
		ref var destStart = ref MemoryMarshal.GetReference(destination);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			Unsafe.Add(ref destStart, i) = mapper(Unsafe.Add(ref sourceStart, i));
			Unsafe.Add(ref destStart, i + 1) = mapper(Unsafe.Add(ref sourceStart, i + 1));
			Unsafe.Add(ref destStart, i + 2) = mapper(Unsafe.Add(ref sourceStart, i + 2));
			Unsafe.Add(ref destStart, i + 3) = mapper(Unsafe.Add(ref sourceStart, i + 3));
		}

		// fill reminder
		for (; i < count; i++)
			Unsafe.Add(ref destStart, i) = mapper(Unsafe.Add(ref sourceStart, i));
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void CloneInPlaceValueType<T>(Span<T> source) where T : ICacheClonable<T> {
		var count = (nuint)source.Length;
		var destination = source;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);
		ref var destStart = ref MemoryMarshal.GetReference(destination);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			Unsafe.Add(ref destStart, i) = Unsafe.Add(ref sourceStart, i).Clone();
			Unsafe.Add(ref destStart, i + 1) = Unsafe.Add(ref sourceStart, i + 1).Clone();
			Unsafe.Add(ref destStart, i + 2) = Unsafe.Add(ref sourceStart, i + 2).Clone();
			Unsafe.Add(ref destStart, i + 3) = Unsafe.Add(ref sourceStart, i + 3).Clone();
		}

		// fill reminder
		for (; i < count; i++)
			Unsafe.Add(ref destStart, i) = Unsafe.Add(ref sourceStart, i).Clone();
	}


	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void CloneInPlaceValueType<T, TCloner>(Span<T> source, TCloner cloner) where TCloner : ICloner<T> {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			cloner.Clone(ref Unsafe.Add(ref sourceStart, i));
			cloner.Clone(ref Unsafe.Add(ref sourceStart, i + 1));
			cloner.Clone(ref Unsafe.Add(ref sourceStart, i + 2));
			cloner.Clone(ref Unsafe.Add(ref sourceStart, i + 3));
		}

		// fill reminder
		for (; i < count; i++)
			cloner.Clone(ref Unsafe.Add(ref sourceStart, i));
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void CloneInPlaceRefType<T>(Span<T> source) where T : ICacheClonable<T> {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			ref var s0 = ref Unsafe.Add(ref sourceStart, i);
			ref var s1 = ref Unsafe.Add(ref sourceStart, i + 1);
			ref var s2 = ref Unsafe.Add(ref sourceStart, i + 2);
			ref var s3 = ref Unsafe.Add(ref sourceStart, i + 3);
			if (s0 is not null) s0 = s0.Clone();
			if (s1 is not null) s1 = s1.Clone();
			if (s2 is not null) s2 = s2.Clone();
			if (s3 is not null) s3 = s3.Clone();
		}

		// fill reminder
		for (; i < count; i++) {
			ref var s = ref Unsafe.Add(ref sourceStart, i);
			if (s is not null) s = s.Clone();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void CloneInPlaceRefType<T, TCloner>(Span<T> source, TCloner cloner) where TCloner : ICloner<T> {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			ref var s0 = ref Unsafe.Add(ref sourceStart, i);
			ref var s1 = ref Unsafe.Add(ref sourceStart, i + 1);
			ref var s2 = ref Unsafe.Add(ref sourceStart, i + 2);
			ref var s3 = ref Unsafe.Add(ref sourceStart, i + 3);
			if (s0 is not null) cloner.Clone(ref s0);
			if (s1 is not null) cloner.Clone(ref s1);
			if (s2 is not null) cloner.Clone(ref s2);
			if (s3 is not null) cloner.Clone(ref s3);
		}

		// fill reminder
		for (; i < count; i++) {
			ref var s = ref Unsafe.Add(ref sourceStart, i);
			if (s is not null) cloner.Clone(ref s);// Unsafe.Add(ref destStart, i) = s.Clone();
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void DisposeInPlace<T>(Span<T> source) where T : IDisposable {
		if (typeof(T).IsValueType)
			DisposeInPlaceValueType(source);
		else
			DisposeInPlaceRefType(source);
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void DisposeInPlaceValueType<T>(Span<T> source) where T : IDisposable {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			Unsafe.Add(ref sourceStart, i).Dispose();
			Unsafe.Add(ref sourceStart, i + 1).Dispose();
			Unsafe.Add(ref sourceStart, i + 2).Dispose();
			Unsafe.Add(ref sourceStart, i + 3).Dispose();
		}

		// fill reminder
		for (; i < count; i++)
			Unsafe.Add(ref sourceStart, i).Dispose();
	}

	[MethodImpl(MethodImplOptions.AggressiveOptimization)]
	private static void DisposeInPlaceRefType<T>(Span<T> source) where T : IDisposable {
		var count = (nuint)source.Length;

		ref var sourceStart = ref MemoryMarshal.GetReference(source);

		nuint i = 0;
		var unrolledEnd = count >= 4 ? count - 3 : 0;

		// unroll loop
		for (; i < unrolledEnd; i += 4) {
			ref var s0 = ref Unsafe.Add(ref sourceStart, i);
			ref var s1 = ref Unsafe.Add(ref sourceStart, i + 1);
			ref var s2 = ref Unsafe.Add(ref sourceStart, i + 2);
			ref var s3 = ref Unsafe.Add(ref sourceStart, i + 3);
			if (s0 is not null) s0.Dispose();
			if (s1 is not null) s1.Dispose();
			if (s2 is not null) s2.Dispose();
			if (s3 is not null) s3.Dispose();
		}

		// fill reminder
		for (; i < count; i++) {
			ref var s = ref Unsafe.Add(ref sourceStart, i);
			if (s is not null) s.Dispose();
		}
	}
}