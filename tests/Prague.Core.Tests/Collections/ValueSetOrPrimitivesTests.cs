namespace Prague.Core.Tests.Collections;

using Prague.Core.Collections;
using NUnit.Framework;

[TestFixture]
public class ValueSetOrPrimitivesTests {
	[Test]
	public void IntersectWith_TwoRefs_KeepsElementsInUnion() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 1, 2, 3, 4, 5 }) self.Add(v);

		var v1 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);

		var v2 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 5, 9 }) v2.Add(v);

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(3));
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);
		Assert.That(self.Contains(5), Is.True);
		Assert.That(self.Contains(1), Is.False);
		Assert.That(self.Contains(4), Is.False);
		Assert.That(self.Contains(9), Is.False);

		self.Dispose();
		v1.Dispose();
		v2.Dispose();
	}

	[Test]
	public void IntersectWith_TwoRefs_OneUninitialized_KeepsElementsInOther() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 1, 2, 3, 4 }) self.Add(v);

		var v1 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);

		var v2 = default(ValueSet<int, DefaultKeyComparer<int>>); // uninitialized

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(2));
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);

		self.Dispose();
		v1.Dispose();
	}

	[Test]
	public void IntersectWith_TwoRefs_BothUninitialized_LeavesSelfUnchanged() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 1, 2, 3 }) self.Add(v);
		var beforeCount = self.Count;

		var v1 = default(ValueSet<int, DefaultKeyComparer<int>>);
		var v2 = default(ValueSet<int, DefaultKeyComparer<int>>);

		self.IntersectWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(beforeCount));
		self.Dispose();
	}

	[Test]
	public void UnionWith_TwoRefs_AddsBoth() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		self.Add(1);

		var v1 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);

		var v2 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 3, 4 }) v2.Add(v);

		self.UnionWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(4));
		Assert.That(self.Contains(1), Is.True);
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);
		Assert.That(self.Contains(4), Is.True);

		self.Dispose();
		v1.Dispose();
		v2.Dispose();
	}

	[Test]
	public void UnionWith_TwoRefs_OneUninitialized_AddsOther() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		var v1 = new ValueSet<int, DefaultKeyComparer<int>>();
		foreach (var v in new[] { 2, 3 }) v1.Add(v);
		var v2 = default(ValueSet<int, DefaultKeyComparer<int>>);

		self.UnionWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(2));
		Assert.That(self.Contains(2), Is.True);
		Assert.That(self.Contains(3), Is.True);

		self.Dispose();
		v1.Dispose();
	}

	[Test]
	public void UnionWith_TwoRefs_BothUninitialized_NoOp() {
		var self = new ValueSet<int, DefaultKeyComparer<int>>();
		self.Add(1);
		var v1 = default(ValueSet<int, DefaultKeyComparer<int>>);
		var v2 = default(ValueSet<int, DefaultKeyComparer<int>>);

		self.UnionWith(ref v1, ref v2);

		Assert.That(self.Count, Is.EqualTo(1));
		self.Dispose();
	}
}
