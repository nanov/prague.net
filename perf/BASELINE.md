# Prague performance baseline
_Generated 2026-07-21T17:59:01Z_

## apple-m4pro-darwin

### core-only  
`Apple M4 Pro` · `Darwin 25.2.0 Darwin Kernel Version 25.2.0: Tue Nov 18 21:09:56 PST 2025; root:xnu-12377.61.12~1/RELEASE_ARM64_T6041` · `.NET 9.0.17` · commit `a20ccd5`
| metric | value | unit |
|---|---:|---|
| ingest.throughput | 8047942.55 | ent/s |
| ingest.alloc | 265.84 | bytes |
| query.uniqueLookup.p50 | 215.31 | ns |
| query.uniqueLookup.alloc | 0.00 | bytes |
| query.rangeScan.p50 | 3002.11 | ns |
| query.rangeScan.alloc | 64.00 | bytes |
| query.joinOne.p50 | 8717.22 | ns |
| query.joinOne.alloc | 64.00 | bytes |
| query.joinMany.p50 | 104260.25 | ns |
| query.joinMany.alloc | 64.00 | bytes |
| query.multiJoin.p50 | 110658.99 | ns |
| query.multiJoin.alloc | 65.00 | bytes |

### full-sim  
`Apple M4 Pro` · `Darwin 25.2.0 Darwin Kernel Version 25.2.0: Tue Nov 18 21:09:56 PST 2025; root:xnu-12377.61.12~1/RELEASE_ARM64_T6041` · `.NET 9.0.17` · commit `a20ccd5`
| metric | value | unit |
|---|---:|---|
| ingest.throughput | 1461211.48 | ent/s |
| query.multiJoin.p50 | 121503.00 | ns |
| query.multiJoin.p99 | 317807.00 | ns |
| query.multiJoin.p999 | 531135.00 | ns |

## linux-x64-ci

### core-only  
`github-ubuntu` · `Ubuntu 24.04.4 LTS` · `.NET 9.0.18` · commit `1157a88`
| metric | value | unit |
|---|---:|---|
| ingest.throughput | 3571997.51 | ent/s |
| ingest.alloc | 254.13 | bytes |
| query.uniqueLookup.p50 | 380.39 | ns |
| query.uniqueLookup.alloc | 0.00 | bytes |
| query.rangeScan.p50 | 6313.04 | ns |
| query.rangeScan.alloc | 64.00 | bytes |
| query.joinOne.p50 | 19033.55 | ns |
| query.joinOne.alloc | 64.00 | bytes |
| query.joinMany.p50 | 349199.58 | ns |
| query.joinMany.alloc | 67.00 | bytes |
| query.multiJoin.p50 | 367428.21 | ns |
| query.multiJoin.alloc | 67.00 | bytes |

### full-sim  
`github-ubuntu` · `Ubuntu 24.04.4 LTS` · `.NET 9.0.18` · commit `1157a88`
| metric | value | unit |
|---|---:|---|
| ingest.throughput | 1091215.71 | ent/s |
| query.multiJoin.p50 | 386975.00 | ns |
| query.multiJoin.p99 | 825919.00 | ns |
| query.multiJoin.p999 | 992095.00 | ns |
