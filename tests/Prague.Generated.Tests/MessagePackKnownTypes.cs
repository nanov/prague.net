// Attribute-less polymorphic types resolve through Prague's typeless chain at runtime; the v4 analyzer
// only knows the default chain, so declare the coverage explicitly.
[assembly: MessagePack.MessagePackKnownType(typeof(Prague.Tests.Models.IExternalTelemetry))]
[assembly: MessagePack.MessagePackKnownType(typeof(Prague.Tests.Models.ExternalBaseTelemetry))]
