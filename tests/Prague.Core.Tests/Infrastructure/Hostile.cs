namespace Prague.Core.Tests.Infrastructure;

using System.Collections;

/// <summary>Enumerable that yields <paramref name="throwAfter"/> items, then throws.</summary>
internal sealed class ThrowingEnumerable<T>(IEnumerable<T> source, int throwAfter) : IEnumerable<T> {
	public IEnumerator<T> GetEnumerator() {
		var yielded = 0;
		foreach (var item in source) {
			if (yielded++ >= throwAfter)
				throw new InvalidOperationException("hostile enumerable");
			yield return item;
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
