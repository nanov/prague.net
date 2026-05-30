namespace Prague.Core;

using System.Diagnostics.Contracts;

public interface IRangeQueryBuilder<TIndexKey> where TIndexKey : IComparable<TIndexKey> {
	public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l);
}

public struct RangeQueryBuilder<TIndexKey> where TIndexKey : IComparable<TIndexKey> {
	private Range _range;

	internal struct Range {
		private readonly RangeValue<TIndexKey> _g;
		private readonly RangeValue<TIndexKey> _l;

		private Range(RangeValue<TIndexKey> g, RangeValue<TIndexKey> l) {
			(_g, _l) = (g, l);
		}

		internal Range Gte(TIndexKey gte) => new(new RangeValue<TIndexKey>(RangeValueType.ThanOrEqual, gte), _l);

		internal Range Gt(TIndexKey gt) => new(new RangeValue<TIndexKey>(RangeValueType.Than, gt), _l);

		internal Range Lt(TIndexKey lt) => new(_g, new RangeValue<TIndexKey>(RangeValueType.Than, lt));

		internal Range Lte(TIndexKey lte) => new(_g, new RangeValue<TIndexKey>(RangeValueType.ThanOrEqual, lte));

		[Pure]
		public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l) => (g, l) = (_g, _l);
	}


	public RangeQueryBuilder() {
		_range = new Range();
	}

	public RangeQueryBuilderGt Gt(TIndexKey value) => new(_range.Gt(value));

	public RangeQueryBuilderGt Gte(TIndexKey value) => new(_range.Gte(value));

	public RangeQueryBuilderLt Lt(TIndexKey value) => new(_range.Lt(value));

	public RangeQueryBuilderLt Lte(TIndexKey value) => new(_range.Lte(value));


	public struct RangeQueryBuilderGt : IRangeQueryBuilder<TIndexKey> {
		private Range _range;

		internal RangeQueryBuilderGt(Range range) {
			_range = range;
		}

		public RangeQueryBuilderDone Lt(TIndexKey value) => new(_range.Lt(value));

		public RangeQueryBuilderDone Lte(TIndexKey value) => new(_range.Lte(value));

		public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l) => _range.Deconstruct(out g, out l);
	}

	public struct RangeQueryBuilderLt : IRangeQueryBuilder<TIndexKey> {
		private Range _range;

		internal RangeQueryBuilderLt(Range range) {
			_range = range;
		}

		public RangeQueryBuilderDone Gt(TIndexKey value) => new(_range.Gt(value));

		public RangeQueryBuilderDone Gte(TIndexKey value) => new(_range.Gte(value));

		public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l) => _range.Deconstruct(out g, out l);
	}

	public struct RangeQueryBuilderDone : IRangeQueryBuilder<TIndexKey> {
		private Range _range;

		internal RangeQueryBuilderDone(Range range) {
			_range = range;
		}

		public void Deconstruct(out RangeValue<TIndexKey> g, out RangeValue<TIndexKey> l) => _range.Deconstruct(out g, out l);
	}
}
