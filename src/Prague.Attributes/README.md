# Prague.Attributes

> Part of **[Prague](https://github.com/nanov/prague.net)** — a high-performance, compile-time-safe
> in-memory event cache for .NET, fed by event-sourced Kafka streams.

The public attribute surface that drives code generation: `[DataCache]`, `[DataCacheKey]`,
`[DataCacheIndex]`, `[DataCacheForeignKey]`, and friends. Annotate a `partial` POCO and the Prague
source generator emits its strongly-typed cache, index storage, and fluent `Query()` API.

```csharp
[DataCache]
public partial class Order {
  [DataCacheKey] public long Id { get; init; }
  [DataCacheIndex] public string CustomerId { get; init; }
}
```

## Install
`dotnet add package Prague.Attributes`

See the [project README](https://github.com/nanov/prague.net) for the full guide.
