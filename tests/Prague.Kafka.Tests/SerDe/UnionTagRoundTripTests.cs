namespace Prague.Kafka.Tests.SerDe;

using Prague.Kafka;
using MessagePack;
using NUnit.Framework;

/// <summary>Prague's chain has no typeless tier: polymorphic members round-trip through [UnionTag].</summary>
[TestFixture]
public class UnionTagRoundTripTests {
	[Test]
	public void InterfaceMember_WithUnionTags_RoundTripsConcreteType() {
		var sent = new Holder { Shape = new Circle { Radius = 3 } };

		var bytes = MessagePackSerializer.Serialize(sent, PragueMessagePack.Options);
		var back = MessagePackSerializer.Deserialize<Holder>(bytes, PragueMessagePack.Options);

		Assert.That(back.Shape, Is.TypeOf<Circle>());
		Assert.That(((Circle)back.Shape!).Radius, Is.EqualTo(3));
	}

	[MessagePackObject]
	public sealed class Holder {
		[Key(0)] public IShape? Shape { get; set; }
	}

	[MessagePackObject]
	[UnionTag<Circle>(0)]
	[UnionTag<Square>(1)]
	public interface IShape { }

	[MessagePackObject]
	public sealed class Circle : IShape {
		[Key(0)] public int Radius { get; set; }
	}

	[MessagePackObject]
	public sealed class Square : IShape {
		[Key(0)] public int Side { get; set; }
	}
}
