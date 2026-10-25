PRAGUE — README FOR LLMS AND CODING ASSISTANTS
==============================================

Audience: a language model helping someone use or modify Prague. Dense, factual, no marketing.
Everything here was checked against the source on 2026-10-22 (library version 0.6.0, packages on
NuGet as 0.6.0-pre.*). When this file and the code disagree, the code wins; say so and read the code.

Repository: https://github.com/nanov/prague.net
Human README: README.md (same repo). Deeper engineering notes: context/*.md. Docs site sources:
www/docs/articles/**. Design specs: docs/superpowers/specs/.


0. ONE-PARAGRAPH SUMMARY
------------------------
Prague is an in-process, in-memory event cache for .NET (net9.0 and net10.0, C# 14). You annotate a
`partial` POCO with [DataCache]; a Roslyn source generator emits a strongly typed cache class
("XxxCache") with index storage, a fluent compile-time-checked Query() builder, a Prepare() builder
for build-once/execute-many queries, generated deep equality (CacheEquals) and Clone(). Caches are
usually hydrated from compacted Kafka topics by Prague.Kafka (one topic per cache, one partition per
topic). Queries intersect indices, return pooled zero-allocation QueryResults<T> that the caller must
Dispose(), and can join caches through generated JoinWith{T}() methods. Not a server, not a
distributed cache, not SQL, no eviction.


1. PACKAGES
-----------
Prague            facade; references Prague.Core + Prague.Attributes + Prague.Codegen. Add this.
Prague.Core       runtime: InMemoryDataCache<TKey,TValue>, indices, query engine, prepared/frozen
                  queries, joins, QueryResults<T>, DataCacheRegistry.
Prague.Attributes the attributes, namespace Prague.Core (yes: the attributes live in namespace
                  Prague.Core even though the package is Prague.Attributes).
Prague.Codegen    source generator, netstandard2.0, referenced as analyzer.
Prague.Kafka      AddKafkaCaches DI, raw zero-copy consumer/producer on the Nanov.Confluent.Kafka
                  fork (namespace stays Confluent.Kafka), filters, health checks, OpenTelemetry.
Prague.Api        MapPragueEndpoints(): GET /prague, /prague/{CacheName}, /prague/{CacheName}/{id}.
Prague.Api.UI     AddPragueUI()/MapPragueUI(): MudBlazor UI over Prague.Api.

Consumer projects: `using Prague.Core;` for everything (attributes, cache types, query extensions).


2. DECLARING A CACHE (attributes, exact forms)
----------------------------------------------
Rules that are compile errors or silent failures if broken:
  * Every [DataCache] class MUST be `partial`.
  * Exactly one property carries [DataCacheKey]. TKey : notnull, IEquatable<TKey>.
  * Do NOT implement any interface by hand (no IDataCacheItem, no GetKey()). The generator adds
    ICacheEquatable<T> / ICacheClonable<T> partials itself.
  * Wrapper class name defaults to "{ClassName}Cache"; override with [DataCache("MyCache")].

Attributes (all in namespace Prague.Core):
  [DataCache]                                   class. Marks a cache item.
  [DataCache(string cacheClassName)]            class. Same, custom wrapper name.
  [DataCache<TExternal>(nameof(TExternal.Id))]  class (a `partial` class you own) — cache for a type you
                                                cannot annotate. Index attributes then go on this class
                                                in their (propertyName, ...) forms.
  [DataCacheKey]                                property. Primary key.
  [DataCacheIndex]                              property. Secondary index. Default IndexType = Many.
  [DataCacheIndex(DataCacheIndexType.Unique|Many|Range)]
  [DataCacheIndex(Many, int initialCapacity)]   per-key bucket capacity hint (Many only; other types
                                                → compile error).
  [DataCacheIndex(Many|Unique, Symmetric = true)] adds reverse map (TKey → index value); needed for
                                                left-index-driven joins. ManyToOne FKs get it for free.
  [DataCacheIndex(nameof(T.Prop), type)] / (nameof(T.Prop), "IndexName", type)   external-type forms.
  [DataCacheHasValueIndex] / [DataCacheHasNotValueIndex]    property (or class w/ propertyName).
                                                Key-set index: keys whose property is non-null/null
                                                (bool: true/false). Query verb WithXxx() / WithoutXxx().
  [DataCacheValueIndex(CompareOp op, object constant)] / [DataCacheNoValueIndex(...)]
                                                Key-set index of keys where `Prop op constant` holds /
                                                does not hold. CompareOp: Equal, NotEqual, GreaterThan,
                                                GreaterThanOrEqual, LessThan, LessThanOrEqual.
                                                Optional IndexName; verb With{IndexName}() / Without{IndexName}().
  [DataCacheForeignKey<T>(DataCacheJoinType)]   property holding the FK. See §7.
  [DataCacheForeignKey<T>(joinType, nameof(T.FkProp))]   "dual form" — placed on the TARGET's PK when you
                                                cannot annotate the FK owner; equivalent to the direct
                                                form on T.FkProp with inverted cardinality.
  [DataCacheForeignKey<T, TSelector>(OneToOne)] selector form; TSelector : readonly struct,
                                                IForeignKeySelector<TFk,TPk> with static abstract
                                                TPk Select(TFk). v1: OneToOne only, on the PK property.
  [DataCacheGlobalLastUpdateIndex<TIndex>]      property = group key. TIndex is a `partial class :
                                                IDataCacheGlobalLastUpdateIndex<TGroupKey>` the generator
                                                completes. Optional (nameof(TimestampProp)) ctor arg.
  [DataCacheGlobalLastUpdateIndex<TIndex, TFilter>]   filtered variant; TFilter : readonly struct,
                                                IDataCacheGlobalLastUpdateFilter<TEntity> (static abstract
                                                bool Include(TEntity)). Membership re-evaluated per update.
  [DataCacheSort("Name", "Prop:1", "Other:-1")] class, AllowMultiple. Emits
                                                `public static readonly IComparer<T> NameComparer` on
                                                the wrapper. ":1" asc, ":-1" desc.
  [DataCacheIgnoreEquality]                     property. Excluded from CacheEquals/CacheGetHashCode.
  [DataCacheTopic] / [DataCacheTopic("orders.[v:env]")] class. Emits `public const string
                                                TopicNameTemplate`. Default template "Cache.<ClassName>".
                                                `[v:name]` resolves from KafkaCachesOptions.Vars, `[e:NAME]` from env vars.
  [DataCacheFromTimestamp]                      property (long). Filled from the Kafka message timestamp.
  [DataCacheHeader("header-name")]              property. Filled from a Kafka header (MessagePack-first
                                                numeric SerDe for int/long).
  [DataCacheFrom<TSource>]                      class. IDE refactoring helper: copy properties from TSource.
  [DataCacheJsonSerializables]                  on a partial JsonSerializerContext: registers
                                                QueryResults<T> types for System.Text.Json source gen.

DataCacheJoinType: OneToOne, OneToMany (legacy, target's perspective), ManyToOne (canonical, FK
holder's perspective).


3. WHAT THE GENERATOR EMITS (names you can rely on)
---------------------------------------------------
For [DataCache] class Order with key OrderId, indices CustomerId (Many), CreatedAt (Range),
InvoiceNumber (Unique), sort "ByPriority":

  class OrderCache : IDataCache<OrderCache, string, Order>
    ctor OrderCache(DataCacheStatisticsCollector? statisticsCollector = null)   → `new OrderCache()` works
    InMemoryDataCache<string, Order> Cache          the raw store
    CacheKeyValueListIndex<...>  CustomerIdIndex    .GetValues(key) → IReadOnlyCollection<TKey>
    CacheRangeIndex<...>         CreatedAtIndex     .GetValuesGte/Gt/Lte/Lt/Between(...), TryGetMin/Max
    CacheUniqueIndex<...>        InvoiceNumberIndex .TryGetValue(indexKey, out TKey), ContainsKey
    void AddOrUpdate(Order doc)                     timestamp = now
    void AddOrUpdate(Order doc, long timestampMs)
    bool AddOrUpdate(Order doc, out Order? old)     true = Add or Update, false = structurally Same
    Query()                                         eager fluent builder
    Prepare()  /  Prepare<TArgs>()                  prepared builder (TArgs : struct)
    static readonly IComparer<Order> ByPriorityComparer
    const string TopicNameTemplate; string TopicTemplate
    IQueryParser<Order> QueryParser                 .StringQueryPooled("customerId=42&total>100")
    void AddJoinedCaches(...)                       only when this cache has FK joins; the registry /
                                                    AddKafkaCaches call it for you
  Query builder extension methods on OrderCache's builder:
    WithOrderId(...) / WithKey(...)                 by primary key
    WithCustomerId(T) | (T? — null skips the lane) | (ReadOnlySpan<T>) | (T[]) | (List<T>)   multi = OR
    WithInvoiceNumber(...)                          same overloads
    WithCreatedAt(q => q.Gte(a).Lt(b))              range sub-builder
    WithXxx() / WithoutXxx()                        key-set indices
    UpdatedAfter(long|DateTime|DateTimeOffset [, out long max]) / UpdatedAfter(after, untilInclusive)
                                                    only when a global last-update index sits on the PK
    JoinWith{T}() / InnerJoinWith{T}()              from FKs, 3 overloads each (no filter, filter, filter+arg)
    CountJoinWith{T}() / InnerCountJoinWith{T}()    for every JoinWith{T} with a QueryResults slot: slot is int (right count), same 3 overloads
  partial class Order : ICacheEquatable<Order>, ICacheClonable<Order>
    bool CacheEquals(Order? other); int CacheGetHashCode(); Order Clone()
  With Prague.Kafka referenced, extension methods:
    orderCache.AddOrUpdateAndProduce(order)         local write; produces ONLY if changed
    orderCache.AddOrUpdateAndProduce(order, force: true)   local write; produces ALWAYS
    orderCache.ForceProduceAll() → int              REPAIR ONLY: snapshot every resident record (all store
                                                    locks held during the copy), produce each one, return count
    orderCache.RemoveAndProduce(key) → bool         local remove + tombstone (published either way)
    IEnrichable<Order>                              header/timestamp enrichment

Generated files are under obj/ (never committed, never edited). Prague.Core also ships T4-generated
JoinQueryBuilders.generated.cs / JoinResults.generated.cs (combinatorial join builders; a query chains
at most MaxJoins = 5 joins, result structs go to MaxJoinResults = 15 slots). Never hand-edit *.generated.cs.


4. WRITING
----------
  cache.AddOrUpdate(doc [, timestampMs] [, out old])
  cache.Cache.AddOrUpdate(key, value [, timestampMs] [, out old]) → bool changed
  cache.Cache.Remove(key [, timestampMs] [, out value])
  cache.Cache.TryGet(key, out value)
  cache.Cache.TryGet(ICollection<TKey> keys [, Predicate<TValue>]) → List<TValue>      (only found)
  cache.Cache.TryGet(ICollection<TKey> keys, Span<TValue> results [, predicate]) → int  (hit count)
Semantics: AddOrUpdate compares with CacheEquals; equal → nothing swapped, no index touched, result
"Same". Timestamps are Unix ms and feed LastUpdated indices.
  cache.AddOrUpdateAndProduce(doc)               local conditional write, Produce only if it changed
  cache.AddOrUpdateAndProduce(doc, force: true)  same local write, Produce ALWAYS (re-publish current
                                                 state; consumers still apply it through CacheEquals)
  cache.ForceProduceAll() → int                  REPAIR ONLY: re-seed the topic with every resident record
                                                 (one snapshot, then one Produce per key; no local write)


5. READING — THE EAGER BUILDER
------------------------------
  cache.Query()
    .WithXxx(...)              candidate-narrowing lane; lanes intersect (AND); first empty lane
                               short-circuits the whole query
    .UseIndex(index, value | span | list [, keySelector])   same, by index field (raw cache has no WithXxx)
    .Or(b1, b2) / .Or(static (b, a) => ..., static (b, a) => ..., arg)   union of two branches,
                               intersected with the rest; branches: WithXxx/UseIndex/nested Or ONLY
    .Where(v => bool)          residual filter on survivors (runs after all index lanes)
    .Sort(IComparer<T>) | .SortBounded(IComparer<T>)   see §6
    .JoinWith{T}() ...         see §7
  terminals (all take `int skip = 0, int take = int.MaxValue`):
    .Execute()                 allocating, no Dispose needed
    .ExecuteCloned()           allocating, rows cloned
    .ExecutePooled()           ArrayPool-backed → MUST Dispose (use `using var`)
    .ExecutePooledCloned()
    .Count()                   int, no materialization
Return type QueryResults<T> (readonly struct, IList<T>, IReadOnlyList<T>, IDisposable):
  Count, TotalCount (pre-paging), Truncated, this[i], AsSpan(), AsMemory(), Slice(i[,n]),
  Sort<TComparer>(cmp), Map<TMapped>(Func) — Map CONSUMES/disposes the source and returns a new result
  of the same pooled kind, so dispose the mapped one —, ToArray(), ToPooledArray(), ToList(),
  ToHashSet(), ToDictionary(...), CloneElements(), implicit → ReadOnlySpan<T>,
  ref enumerator (`foreach (ref var x in results)` edits in place). QueryResults<T>.Empty exists.
Range sub-builder (type-state): start with Gt/Gte OR Lt/Lte, then optionally the other side.
  There is NO Between, NO Eq, NO Or on the range builder. `q => q.Gte(a).Lt(b)` is [a, b).
Multi-value WithXxx(span/list) is OR within that lane. Place the most selective lane first on the
eager path (the order is the plan). Where never uses an index.


6. SORTING AND PAGING
---------------------
  .Sort(cmp)          classic: materialize, stable full sort, slice. For whole results or large takes.
  .SortBounded(cmp)   top-k selection for a page. For small `take` relative to matches: 2.3–8.6x faster.
                      Slower (1.3–2.7x) and rents O(N) when take ≈ a quarter or more of the matches.
Both break ties by encounter order; a TOTAL comparer (fall back to PK) gives identical pages across
plans. A struct comparer (`readonly struct : IComparer<T>`) makes either sort 0 B; a class comparer
costs ~88–168 B per query. After Sort/SortBounded only the OUTER, UNFILTERED JoinWith{T}() binds.
Generated comparers: XxxCache.{Name}Comparer from [DataCacheSort].


7. JOINS
--------
Declare on the FK-holding property. Which cache gets which method:
  OneToOne  on scalar FK   → both: TargetCache.JoinWith{Holder}() and HolderCache.JoinWith{Target}(); slot TRight?
  OneToMany on scalar FK   → target cache only: TargetCache.JoinWith{Holder}(); slot QueryResults<THolder>
  ManyToOne on scalar FK   → holder: JoinWith{Target}() slot TRight?; target: JoinWith{Holder}() slot QueryResults
  ManyToOne on List<TKey>  → both directions, slot QueryResults<TRight>
Result rows: JoinResult<TLeft, T1 [, T2, ...]> with fields `Left`, `Right` (FIRST joined slot — not
Right1), `Right2`, `Right3`, ... Outer join → slot null / empty when no match; InnerJoinWith{T}
drops the row instead.
Count form: where JoinWith{T} yields QueryResults<TRight>, CountJoinWith{T}() yields the count as int
(InnerCountJoinWith{T}() drops zero counts); same filter overloads; eager, prepared and frozen alike;
unfiltered it is one index-bucket read per left, no rights materialised. No nested-continuation form.
Filter overloads: .JoinWith{T}(q => q.WithStatus(x)) and .JoinWith{T}(static (q, arg) => ..., arg).
The callback gets a NON-executable builder: only WithXxx / UseIndex / Or; no Where, no Execute.
Lower level for raw caches: .JoinOne(rightCache, ...) / .InnerJoinOne / .JoinMany / .InnerJoinMany /
.CountJoin / .InnerCountJoin (+ Collection / CollectionForward variants) —
pass the cache WRAPPER, not rightCache.Query().
Wiring: caches built by DataCacheRegistryBuilder or AddKafkaCaches are joined automatically;
`new XxxCache()` instances need `left.AddJoinedCaches(right, ...)` once before joining.
Pooled join results share one rented buffer; one Dispose() on the QueryResults returns everything.
At most 5 joins per query.


8. PREPARED AND FROZEN QUERIES
------------------------------
  var q = cache.Prepare<TArgs>()          // or Prepare() with TArgs = NoArgs
      .WithXxx(static a => a.Field)      // parameterized; bound forms WithXxx(value) also exist
      .WithRange(static (rb, a) => rb.Gte(a.Since))
      .WithRange(static a => a.From, static a => a.To)        // optional bounds; null = open side
      .Where(static (v, in a) => v.Price >= a.Min)            // NOTE `in a`, type ArgFilter<TValue,TArgs>
      .Or(b => ..., b => ...) / .If(cond, b => ...) / .IfElse(cond, t, e)
      .Match(static a => a.Mode, m => m.Case(Mode.X, b => ...).Default(b => ...))      // tag form
      .Match(m => m.Case(static a => a.Min > 0, b => ...).Case(...).Default())         // guard form
      .Sort(cmp) | .SortBounded(cmp)
      .JoinWith{T}()
      .Build()          → PreparedQuery<TArgs,TResult>   replays into the eager engine; same rows+order
      .BuildFrozen([FrozenOptions]) → FrozenQuery<TArgs,TResult>   planned once; executor is PointLookup
                                       (unique eq), Pipeline (single pass, 0 B), or Replay
  q.Execute(in args, skip, take) / ExecutePooled / ExecuteCloned / ExecutePooledCloned / Count(in args)
  frozen.Explain() → string describing plan, executor, optimizations, last seed chosen
HARD RULES (compile errors):
  * TArgs : struct (tuple, readonly record struct, int, NoArgs). Class → CS0453.
  * The arg Where lambda must spell `in`: `static (v, in a) => ...`; `(v, a) =>` → CS1676. A stored
    delegate must be typed ArgFilter<TValue,TArgs>. All other callbacks are plain Func<TArgs, …>.
  * Every Match ends with .Default(...) or .Default(); missing/open chain or Case after Default → CS0315.
  * Inside Or branches: WithXxx/UseIndex, nested Or, narrow-only If/Match. Inside If/Match arms: those
    + Where. Never inside any branch: joins, Sort/SortBounded, Build.
  * Prepared builders have no Execute*; Build()/BuildFrozen() are the only terminals.
  * Write selector lambdas `static` so the delegate is created once at build.
ORDERING CONTRACT of BuildFrozen(): same rows, Count, TotalCount, Truncated as eager; UNSORTED order
is NOT guaranteed and ties of a sorted result are unspecified. Sorted with a total comparer ==
byte-identical to eager. `new FrozenOptions { PreserveEagerOrder = true }` restores eager order at
eager-ish cost. FrozenOptions flags: FuseFilters, AdaptiveFilterOrdering, CapacityHints, Pipeline
(default true); IndexSideProbes, PreserveEagerOrder (default false). Read once at build.
Use a frozen query stored in a field for hot, repeated shapes; it is immutable and thread-safe.


9. GLOBAL LAST-UPDATE INDEX
---------------------------
  public partial class DeptIndex : IDataCacheGlobalLastUpdateIndex<int>;    // generator completes it
  [DataCacheGlobalLastUpdateIndex<DeptIndex>] public int DepartmentId { get; set; }   // on N caches
  registry.GetGlobalIndex<DeptIndex>()  or DI singleton
  index.TryGetMin/TryGetMax(out long ms[, out key]); GetEntitiesCount(key); TryGetMax(ReadOnlySpan<key>, out ms)
  index.Index → LastUpdatedIndex<TGroupKey> (GetValuesGte/Between… in long/DateTime/DateTimeOffset)
Query: ONLY when the group key is the cache's primary key: cache.Query().UseIndex(index | index.Index,
afterMs [, out maxMs] | fromMs, toMs) — the generator also emits cache.Query().UpdatedAfter(...) with
the same shapes (long | DateTime | DateTimeOffset). A non-PK group key is read through the index itself
(index.Index.GetValuesGt(ms) → changed group keys; TryGetMax; GetEntitiesCount). "After" is strictly
greater than. Watermarks across caches are not transactional — take the minimum.


10. REGISTRY (no Kafka)
-----------------------
  var registry = new DataCacheRegistryBuilder().Register<OrderCache>().Register<CustomerCache>().Build();
  var orders = registry.GetCache<OrderCache>();  registry.GetGlobalIndex<T>();  registry.GetCaches()
DataCacheRegistry has no public constructor; use the builder. It wires AddJoinedCaches and shared
global indices between the registered caches.


11. KAFKA (Prague.Kafka)
------------------------
  services.AddKafkaCaches(
      o => { o.BootstrapServers = "kafka:9092"; o.ClientSettings["group.id"] = "svc"; o.Vars["env"] = "prod"; },
      b => {
          b.AddCache<OrderCache, string, Order>()               // topic from [DataCacheTopic] / default
              .WithHeaderEqualsFilter("tenant", "acme")        // string | int | long, 1..n values (OR)
              .WithHeaderNotEqualsFilter("source", "synthetic")
              .WithHeaderExistsFilter("tenantId")              // up to 64 distinct required names
              .WithHeaderFilter<long>("ts", v => v > cutoff, passOnNull: true)
              .WithKeyFilter(static k => ..., treatAsDelete: false)
              .WithValueFilter(static o => o.Active, treatAsDelete: true)
              .WithKeyFilter(static sp => <state>, static (state, k) => ...)     // DI state, resolved ONCE at build
              .WithValueFilter<IPolicy>(static (policy, o) => ...)             // TService must be spelled
              .WithAfterHandler<OrderProjector>();                             // ICacheAfterHandler<string,Order>
          b.AddCache<CustomerCache, int, Customer>("customers-[v:env]");      // explicit topic
          b.AddCache<X, K, V>(sp => <topic from options>);
          b.AddInternalCache<ViewCache, int, View>();                           // in registry, not consumed
      },
      opts => opts.WithMessagePackResolver(def => CompositeResolver.Create(Mine.Instance, def)));   // optional
  AddKafkaCaches(string configsSectionName, ...) overload: multiple clusters, one call each.
  Resolve caches from DI: provider.GetRequiredService<OrderCache>().
  Startup: the hosted service's StartAsync returns after every cache reached initial-load EOF.
  Elsewhere: await host.DataCachesLoadCompletion();
Facts:
  * One partition per cache topic (not enforced; multi-partition topics go live early). Compacted topic;
    message key = cache key; null value = tombstone. Prague never auto-creates topics. Suggested topic
    settings: www/docs/articles/advanced/kafka-integration.md "Creating a topic".
  * Each process consumes under its own group.id and commits no offsets → every instance loads the
    whole topic; restart = full reload. Setting group.id makes instances SPLIT partitions — usually wrong.
  * Filters AND-compose, first-reject-wins, evaluated once per message on the single consume thread
    (do not block/IO in a predicate). Header gate runs before key deserialization and has no
    treatAsDelete. A throwing predicate = reject. Filters never re-apply to data already in memory.
  * Tombstones skip key/value filters and always delete; only an explicitly rejecting header filter or
    the producer self-filter drops them.
  * After-handlers fire on the LIVE phase only. UpdateType: Filtered=0 (no key/values), Same=1,
    Add=2, Update=3, Delete=4. Signature: ValueTask Handle(UpdateType, TKey key, TValue? newValue, TValue? oldValue).
  * Producer: keyed singleton KafkaCacheProducer under the section name ("kafkaCaches" default):
    [FromKeyedServices("kafkaCaches")] KafkaCacheProducer p; p.Produce(topic, key, value); p.Delete(topic, key).
    Fire-and-forget (no delivery report; failures logged). librdkafka defaults acks=all, idempotence off.
    A process NEVER consumes its own writes (X-Producer-Id header self-filter) → use
    cache.AddOrUpdateAndProduce(value) / cache.RemoveAndProduce(key) so the local cache reflects them.
  * Every MessagePack call uses PragueMessagePack.Options; MessagePackSerializer.DefaultOptions is ignored.
  * Health: services.AddHealthChecks().AddPragueKafkaLiveness(tags: [...]).AddPragueKafkaReadiness(tags: [...]);
    KafkaCachesHealthOptions { PollLoopHeartbeatTimeout=3s, HandlerProcessingTimeout=5s, MinBrokersUp=1 }.
    MinBrokersUp needs KafkaCachesGlobalOptions.StatisticsEnabled (default true); set 0 if disabled.
    Default MapHealthChecks returns 200 for Degraded — map Degraded → 503 for k8s readiness.
  * OTel: meterProviderBuilder.AddPragueKafkaInstrumentation([prefix]) → prague.kafka.consumer.{partitions.assigned,
    broker.rtt.p99, health}, prague.kafka.cache.{size, messages.received, health, index.size}.
  * Testing: no in-memory broker substitute (TestAdaptor was removed); use Testcontainers; give each
    consumer under test its own group.id.


12. CONCURRENCY CONTRACT
------------------------
Single writer per cache (the consume thread), lock-free readers, no query-time lock, no snapshot
isolation. A query never throws because of a concurrent write; it may include or miss a row written
mid-query, and two join legs may disagree. QueryResults.Truncated / QueryResultsDiagnostics.DroppedRows
are the floor under slot sizing and are expected to be false / 0.


13. COMMON MISTAKES → CORRECT FORM
----------------------------------
  forgetting `partial` on a [DataCache] class              → add `partial`
  implementing IDataCacheItem / GetKey() by hand           → remove; [DataCacheKey] is enough
  `new DataCacheRegistry()`                                → `new DataCacheRegistryBuilder().Register<..>().Build()`
  `row.Right1`                                             → `row.Right` (then Right2, Right3 …)
  `q => q.Between(a, b)` / `q.Eq(x)` / `q.Gt(a).Or.Lt(b)`  → `q => q.Gte(a).Lt(b)`; disjunction via .Or(...)
  `.Map(...)` on the builder before Execute                → execute first, then `results.Map(...)`
  `results.Items`                                          → QueryResults IS the list: Count / indexer / AsSpan()
  `cache.Cache.TryGetValues(...)`                          → `cache.Cache.TryGet(keys, span)` / `TryGet(keys)`
  `.SortByPriority()`                                      → `.Sort(OrderCache.ByPriorityComparer)` or SortBounded
  `OrderCache.Topic`                                       → `OrderCache.TopicNameTemplate`
  `[DataCacheTopic("a.{version}")]`                        → `[DataCacheTopic("a.[v:version]")]` + o.Vars["version"]
  `services.AddPragueKafka(...).AddConsumer(...)`          → `services.AddKafkaCaches(o => ..., b => b.AddCache<..>())`
  `b.AddOrderCache()`                                      → `b.AddCache<OrderCache, string, Order>()`
  `Prepare<MyArgsClass>()`                                 → make TArgs a struct / record struct / tuple
  `.Where((v, a) => ...)` on a prepared builder            → `.Where(static (v, in a) => ...)`
  Match without Default                                    → end with .Default(...) or .Default()
  Where / Sort / Join inside an Or branch                  → move outside the branch
  ExecutePooled() without Dispose                          → `using var results = ...ExecutePooled();`
  relying on row order of an unsorted BuildFrozen() query  → add Sort/SortBounded with a total comparer,
                                                             or FrozenOptions.PreserveEagerOrder
  multi-partition cache topic                              → recreate with --partitions 1
  expecting the local cache to see producer.Produce(...)   → use AddOrUpdateAndProduce / RemoveAndProduce
  mutating MessagePackSerializer.DefaultOptions for Prague → WithMessagePackResolver(...) on AddKafkaCaches
  editing *.generated.cs or obj/ generated files           → change Prague.Codegen or the .tt template
  MessagePackSerializer.Serialize(...) inside Prague.Kafka without PragueMessagePack.Options → always pass it


14. FOR CONTRIBUTORS (repo map, commands, invariants)
----------------------------------------------------
  dotnet build Prague.sln                 warnings are errors; LangVersion 14; TreatWarningsAsErrors
  dotnet test Prague.Tests.slnf           NUnit everywhere; Kafka.IntegrationTests need Docker
  dotnet run -c Release -f net9.0 --project benchmarks/Prague.Benchmarks -- --inProcess --anyCategories <cat>
  perf/run.sh core|sort|sim|concurrent|all [--bless]      regression tripwire vs perf/baseline/<machine>.json
  T4: from src/Prague.Core: ~/.dotnet/tools/t4 <Name>.tt -o <Name>   (basename only; the template appends
      .generated.cs). T4 does not run on build. After codegen/T4 changes run Prague.Generated.Tests.
Layout: src/{Prague,Prague.Core,Prague.Attributes,Prague.Codegen,Prague.Kafka,Prague.Api,Prague.Api.UI};
  tests/{Prague.Core.Tests (raw POCO, no codegen), Prague.Generated.Tests (through codegen),
  Prague.Kafka.Tests (broker-free), Prague.Kafka.IntegrationTests (Testcontainers), Prague.Tests.Models};
  benchmarks/Prague.Benchmarks (+RESULTS.MD); perf/; context/ (engineering notes); docs/superpowers/
  {specs,plans}; www/docs (DocFX sources); CLAUDE.md (agent guidance + topic→file map).
Invariants: never hand-edit *.generated.cs; every MessagePack call passes PragueMessagePack.Options;
  pooled results are disposed exactly once (joins use the handedOff guard); anything added to a query
  path is decided at build time, not per row — re-measure production shapes A/B after executor changes;
  frozen-vs-eager differential tests are the safety net (sequence assertions use PreserveEagerOrder);
  hot paths follow the house high-performance rules (Span/stackalloc ≤1 KB/ArrayPool via
  PragueArrayPool<T>.Pool, static-abstract strategy structs, no LINQ, no closures, no virtual dispatch).
Read next: context/core.md → context/indexes.md → context/query.md → context/joins.md →
  context/generated.md → context/kafka.md (+ kafka-filters.md, kafka-serde.md, kafka-health.md).
