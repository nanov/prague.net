# Filter-adjacent defects fixed alongside the tombstone gate

Date: 2026-09-14
Status: Implemented — companion to [`2026-09-14-tombstone-gate-design.md`](2026-09-14-tombstone-gate-design.md)

Each of these was found while verifying the tombstone fix, verified independently against the code, and fixed in the same PR because each one either sits on the same line of code or undoes the tombstone contract for some configuration. None changes a public signature.

## 1. Unbounded producer-controlled `stackalloc` on the header name

**Bug.** `KafkaHeaderFilters.ShouldProcess` did `stackalloc char[headerName.Length]` on the raw UTF-8 header name straight off the wire — unconditionally, before the dictionary lookup, even with zero header filters configured. Kafka bounds a header key only by `message.max.bytes` (~1 MiB by default), so a ~512 KiB name is 1 MiB of `char` against a 1 MiB thread stack: an uncatchable `StackOverflow` that kills the host, and the poison record replays from the log into a crash loop. The `stackalloc` is in a callee whose frame is reclaimed on return, so the hazard is the size of one frame, not accumulation across headers.

**Fix.** A length bound computed once at construction: `_maxNameUtf8Length` is the UTF-8 byte count of the longest configured header name, and `ShouldProcess` answers any longer name with `true` before allocating. The bound is exact, not merely conservative: `GetByteCount(GetString(b)) >= b.Length` holds for every byte sequence (well-formed input round-trips; ill-formed input decodes to U+FFFD, which re-encodes to three bytes and never shrinks), so a name over the bound cannot decode to any configured key. The transcode lives in a separate `Resolve` (with `[SkipLocalsInit]`) so the `localloc` does not block inlining of the bound check.

**Side effect.** With no header filters configured the bound is zero, so every name is answered by one integer compare. That closes the separately-claimed "no `IsEmpty` short-circuit" item (measured ~12 ns/header) without a guard around the loop — the loop itself must still run for the producer self-filter.

**Pinned by** `HeaderGateStateTests` — the length-bound group, and the two `[Category("StackSafety")]` cases, which against unfixed code do not fail but kill the test host.

## 2. `WithHeaderExistsFilter` composed as OR

**Bug.** Presence was tracked in a single shared `bool`; `KafkaHeaderExistsFilter` set it without regard to which name resolved. `exists("A") + exists("B")` with only `A` present was admitted, against the documented AND. A second, distinct bug in `KafkaCombinedHeaderFilter` aggregated the flag with AND seeded `true`, so pairing `exists("h")` with any other filter on the *same* name silently dropped the requirement.

**Fix.** A requirement bitmask. Each required name owns one bit in `KafkaHeaderFilters.RequiredMask`; the gate ORs a name's bit into `seen` when it resolves and compares `seen == RequiredMask` once after the last header. Cap is 64 distinct required names; the 65th throws at handler build. The combined filter now aggregates `RequiresHeader` with OR. The bit lives on the dictionary entry (`HeaderFilterEntry`), not on the filter object, because the builder owns the filter instances and hands the same ones over on every container build.

**Behaviour change.** Multiple `WithHeaderExistsFilter` calls now all require their header; a combined filter still requires it.

## 3. `CachesLoadingCount` leaked on rebalance

**Bug.** `KafkaCacheConsumer._cachesLoading` was incremented per assigned partition in the partitions-assigned callback — which fires on every rebalance — and decremented only on a handler's first EOF. One post-load rebalance therefore pinned `HasIncompleteInitialLoad` to Degraded for the life of the process. (The same counter was also the P>1 half of the defect recorded in `context/kafka.md`.)

**Fix.** The mirrored counter is deleted. `AsyncCountdownEvent` already counts handlers and is signalled once per handler; it now publishes `SetCachesLoadingCount` from its constructor and from `Signal`, so the gauge has one source of truth. The gauge now reads N from consumer construction rather than 0 until the first assignment, which is the more honest value before load.

## 4. `RemoveAndProduce` swallowed the tombstone for a non-resident key

**Bug.** The generated `RemoveAndProduce` returned `false` without producing anything when the local cache did not hold the key. Residency is a fact about this process's view — an ingress filter that excludes the key, a load still running, an earlier local removal — not about the log, so for a filtered cache there was no correct "tombstone this key" API at all. The exact mirror of the ingress bug.

**Fix.** The tombstone is always produced; the `bool` narrows to "the local cache held the key". Emitted by `CacheGenerator`; re-run `Prague.Generated.Tests` after touching it.

## Also in the PR

- **Dead code** removed: `KafkaHeaderNotExistsFilter` (never constructed, and a trap next to the invariant the tombstone waiver rests on) and the string-keyed `ShouldProcess` overload (no callers since the raw port).
- **`UpdateType` docs**, which had none — including that `Filtered` carries no key (`default(TKey)`, `null` for a reference type) and that `Delete` fires only for a resident key.
- **A throwing header predicate is now caught** at the `ConsumeRawLoop` call site — see the tombstone spec, *The change*. A predicate that throws while shutdown is already requested is surfaced as a cancellation on the loop's token (`ThrowIfCancellationRequested`), so it takes the graceful stop path rather than latching the consumer fatal.

## Still open — not addressed here

- **`KafkaCacheProducer.Delete` / `Produce` leave the producing process's own cache stale.** The self-filter is absolute by design, so a process that calls the producer directly never sees its own write. `RemoveAndProduce` is the safe path for deletes; there is no equivalent for produce. Bounded to the producing process's lifetime — `InstanceId` is per-process, so a restart re-applies the write. The public docs now point at `RemoveAndProduce`; a `Produce` counterpart is a separate feature.
- **Cache topics with P > 1** — defect (a) in `context/kafka.md` still stands.
