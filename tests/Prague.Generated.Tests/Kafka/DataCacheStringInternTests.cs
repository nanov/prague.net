namespace Prague.Generated.Tests.Kafka;

using MessagePack;
using MessagePack.Resolvers;
using NUnit.Framework;
using Prague.Core;
using Prague.Kafka;

/// <summary>
///   <c>[DataCacheStringIntern(false)]</c> on a partial property: the generator emits the implementing part with
///   <c>[MessagePackFormatter(typeof(PragueStringFormatter))]</c>, so under <see cref="PragueMessagePack.Options"/>
///   that property decodes fresh strings while every other string member stays interned.
/// </summary>
[TestFixture]
public class DataCacheStringInternTests {
	[Test]
	public void Contractless_OptedOutProperty_IsNotInterned() {
		var bytes = MessagePackSerializer.Serialize(
			new ContractlessInternModel { Code = "intern-code", Note = "intern-note", Explicit = "intern-explicit" },
			PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<ContractlessInternModel>(bytes, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<ContractlessInternModel>(bytes, PragueMessagePack.Options);

		Assert.That(a.Note, Is.EqualTo("intern-note"));
		Assert.That(b.Note, Is.Not.SameAs(a.Note));
		Assert.That(b.Code, Is.SameAs(a.Code));
		Assert.That(b.Explicit, Is.SameAs(a.Explicit));
	}

	[Test]
	public void MessagePackObject_OptedOutProperty_IsNotInterned() {
		var bytes = MessagePackSerializer.Serialize(
			new KeyedInternModel { Code = "keyed-code", Note = "keyed-note" }, PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, PragueMessagePack.Options);

		Assert.That(a.Note, Is.EqualTo("keyed-note"));
		Assert.That(b.Note, Is.Not.SameAs(a.Note));
		Assert.That(b.Code, Is.SameAs(a.Code));
	}

	[Test]
	public void AllowPrivateMessagePackObject_OptedOutProperty_IsNotInterned() {
		var bytes = MessagePackSerializer.Serialize(
			new PrivateKeyedInternModel { Code = "private-code", Note = "private-note" }, PragueMessagePack.Options);

		var a = MessagePackSerializer.Deserialize<PrivateKeyedInternModel>(bytes, PragueMessagePack.Options);
		var b = MessagePackSerializer.Deserialize<PrivateKeyedInternModel>(bytes, PragueMessagePack.Options);

		Assert.That(a.Note, Is.EqualTo("private-note"));
		Assert.That(b.Note, Is.Not.SameAs(a.Note));
		Assert.That(b.Code, Is.SameAs(a.Code));
	}

	[Test]
	public void CustomChain_HostResolverFirst_OptOutStillApplies() {
		// The documented WithMessagePackResolver shape: host formatters first, Prague's composite after.
		var options = MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(NativeDateTimeResolver.Instance, PragueMessagePack.CreateDefaultComposite()));
		var bytes = MessagePackSerializer.Serialize(
			new KeyedInternModel { Code = "host-code", Note = "host-note" }, options);

		var a = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, options);
		var b = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, options);

		Assert.That(b.Note, Is.Not.SameAs(a.Note));
		Assert.That(b.Code, Is.SameAs(a.Code));
	}

	[Test]
	public void CustomChain_SourceGeneratedFormattersFirst_OptOutIsIgnored() {
		// Known limitation, pinned: MessagePack's source generator cannot see Prague's generated
		// [MessagePackFormatter], so its formatter reads Note through the resolver's interning string
		// formatter. If this starts failing, MessagePack honors the attribute and the docs can drop the caveat.
		var options = MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(GeneratedMessagePackResolver.Instance, PragueMessagePack.CreateDefaultComposite()));
		var bytes = MessagePackSerializer.Serialize(
			new KeyedInternModel { Code = "sourcegen-code", Note = "sourcegen-note" }, options);

		var a = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, options);
		var b = MessagePackSerializer.Deserialize<KeyedInternModel>(bytes, options);

		Assert.That(options.Resolver.GetFormatter<KeyedInternModel>()!.GetType().Name, Is.EqualTo("KeyedInternModelFormatter"));
		Assert.That(b.Note, Is.SameAs(a.Note));
	}

	[Test]
	public void CustomChain_SourceGeneratedFormattersFirst_SuppressSourceGenerationRestoresOptOut() {
		// The documented workaround: no source-generated formatter, so the type falls through to Prague's composite.
		var options = MessagePackSerializerOptions.Standard.WithResolver(
			CompositeResolver.Create(GeneratedMessagePackResolver.Instance, PragueMessagePack.CreateDefaultComposite()));
		var bytes = MessagePackSerializer.Serialize(
			new SuppressedInternModel { Code = "suppressed-code", Note = "suppressed-note" }, options);

		var a = MessagePackSerializer.Deserialize<SuppressedInternModel>(bytes, options);
		var b = MessagePackSerializer.Deserialize<SuppressedInternModel>(bytes, options);

		Assert.That(b.Note, Is.Not.SameAs(a.Note));
		Assert.That(b.Code, Is.SameAs(a.Code));
	}

	[Test]
	public void OptedOutProperty_WireFormat_IsUnchanged() {
		var model = new ContractlessInternModel { Code = "c", Note = "n", Explicit = "e" };
		var plain = new PlainInternModel { Code = "c", Note = "n", Explicit = "e" };

		Assert.That(MessagePackSerializer.Serialize(model, PragueMessagePack.Options),
			Is.EqualTo(MessagePackSerializer.Serialize(plain, PragueMessagePack.Options)),
			"The generated backing field must not reach the wire.");
	}

	[Test]
	public void OptedOutProperty_KeepsAccessorsAndNullability() {
		var model = new ContractlessInternModel { Code = "c", Note = null, Explicit = "e" };

		Assert.That(model.Note, Is.Null);
		Assert.That(ContractlessInternModel.Shared, Is.Null);
		ContractlessInternModel.Shared = "s";
		Assert.That(ContractlessInternModel.Shared, Is.EqualTo("s"));
	}
}

public partial class ContractlessInternModel {
	public string Code { get; set; } = "";

	[DataCacheStringIntern(false)]
	public partial string? Note { get; init; }

	[DataCacheStringIntern(true)]
	public partial string Explicit { get; set; }

	[DataCacheStringIntern(false)]
	[IgnoreMember]
	public static partial string? Shared { get; set; }
}

public class PlainInternModel {
	public string Code { get; set; } = "";
	public string? Note { get; init; }
	public string Explicit { get; set; } = "";
}

[MessagePackObject]
public partial class KeyedInternModel {
	[Key(0)]
	public string Code { get; set; } = "";

	[Key(1)]
	[DataCacheStringIntern(false)]
	public partial string Note { get; set; }
}

[MessagePackObject(AllowPrivate = true)]
public partial class PrivateKeyedInternModel {
	[Key(0)]
	public string Code { get; set; } = "";

	[Key(1)]
	[DataCacheStringIntern(false)]
	public partial string Note { get; set; }
}

[MessagePackObject(SuppressSourceGeneration = true)]
public partial class SuppressedInternModel {
	[Key(0)]
	public string Code { get; set; } = "";

	[Key(1)]
	[DataCacheStringIntern(false)]
	public partial string Note { get; set; }
}
