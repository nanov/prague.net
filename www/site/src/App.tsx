import {
	Zap,
	ShieldCheck,
	Cpu,
	Network,
	Clock,
	ArrowRight,
	ChevronRight,
	Database,
	Github,
} from "lucide-react";

const BASE = import.meta.env.BASE_URL;
const DOCS_URL = `${BASE}docs/`;
const API_URL = `${DOCS_URL}api/index.html`;
const GITHUB_URL = "https://github.com/nanov/prague.net";
const NUGET_URL = "https://www.nuget.org/packages/Prague/";
const RESULTS_URL = `${GITHUB_URL}/blob/main/benchmarks/Prague.Benchmarks/RESULTS.MD`;

const sample = `[DataCache]
public partial class Product {
    [DataCacheKey] public int Id { get; set; }

    [DataCacheIndex(DataCacheIndexType.Range)]
    public long ReleaseDate { get; set; }

    public bool IsPublished { get; set; }
}

[DataCache]
public partial class Offer {
    [DataCacheKey] public int Id { get; set; }

    [DataCacheForeignKey<Product>(DataCacheJoinType.OneToMany)]
    public int ProductId { get; set; }

    public decimal Price { get; set; }
}

// Generated builder: compile-time checked, pooled, 0 B per execution.
using var rows = productCache.Query()
    .WithReleaseDate(q => q.Gte(now))   // range index
    .Where(p => p.IsPublished)          // residual filter on survivors
    .JoinWithOffer()                    // 1:N join generated from the FK
    .ExecutePooled();                   // rents from ArrayPool; Dispose() returns it

foreach (var row in rows) {
    var product = row.Left;             // Product
    var offers  = row.Right;            // QueryResults<Offer>
}`;

const stats = [
	{ value: "11M+", label: "reads per second, pooled 3-level join, 10 reader threads + 2 writers" },
	{ value: "< 100 ns", label: "indexed lookup" },
	{ value: "18 ns", label: "frozen unique-key point lookup" },
	{ value: "0 B", label: "per execution on pooled and frozen paths" },
];

const comparison: { feature: string; prague: string; redis: string; memoryCache: string }[] = [
	{ feature: "Where data lives", prague: "In your process", redis: "Separate server over the network", memoryCache: "In your process" },
	{ feature: "Query model", prague: "Indexed fluent queries, joins, OR, sorted paging", redis: "Key lookups, Lua scripts", memoryCache: "Key lookups" },
	{ feature: "Type safety", prague: "Generated at compile time", redis: "Runtime strings", memoryCache: "Runtime keys and casts" },
	{ feature: "Allocations per read", prague: "0 B, pooled results", redis: "Serialization + network buffers", memoryCache: "Per object" },
	{ feature: "Source of truth", prague: "Compacted Kafka topic", redis: "Itself", memoryCache: "Itself" },
	{ feature: "Eviction", prague: "None, topic compaction is the retention", redis: "TTL, LRU", memoryCache: "TTL, LRU" },
];

const features = [
	{
		icon: Cpu,
		title: "Extreme performance",
		body: "Source generators do the wiring, so nothing is reflected over on the read path. Spans, stackalloc bitmaps, ArrayPool buffers and lock-free readers keep the hot path allocation-free.",
	},
	{
		icon: ShieldCheck,
		title: "Compile-time safety",
		body: "IntelliSense lists exactly the indices you declared. Which verbs are reachable where is enforced by the type system, not by a runtime check.",
		dark: true,
	},
	{
		icon: Network,
		title: "Built for event sourcing",
		body: "Kafka consumer and producer on a raw, span-based zero-copy path. Header, key and value filters, tombstones, and conditional dispatch that skips unchanged values.",
	},
	{
		icon: Database,
		title: "Automatic query planning",
		body: "Unique, Many and Range indices plus symmetric and key-set variants. Every WithXxx narrows candidates; intersection short-circuits on the first empty lane.",
	},
	{
		icon: Clock,
		title: "Global last-update index",
		body: "Cross-cache change tracking for incremental sync: ask for everything that changed since a timestamp.",
		muted: true,
	},
];

export default function App() {
	return (
		<div className="min-h-screen grid-bg selection:bg-brand-orange selection:text-white pb-20">
			<a href="#main" className="skip-link">Skip to content</a>

			<header className="border-b border-brand-black/10 bg-white/80 backdrop-blur-md sticky top-0 z-50">
				<nav aria-label="Primary" className="max-w-7xl mx-auto px-4 sm:px-6 h-16 sm:h-20 flex items-center justify-between gap-4">
					<a href={BASE} className="flex items-center gap-3 sm:gap-4 min-w-0" aria-label="Prague home">
						<span className="w-10 h-10 shrink-0 bg-brand-black rounded-lg overflow-hidden flex items-center justify-center p-1 border border-white/10 shadow-lg">
							<img src={`${BASE}logo-mark.png`} alt="" width="1408" height="768" className="w-full h-full object-contain invert" />
						</span>
						<span className="text-left min-w-0">
							<span className="block text-2xl font-bold tracking-tighter uppercase italic leading-none">Prague</span>
							<span className="hidden sm:block text-[11px] font-mono uppercase tracking-widest text-brand-black/70">High-performance .NET caching</span>
						</span>
					</a>
					<ul className="flex items-center gap-4 sm:gap-8 text-[11px] font-bold uppercase tracking-widest">
						<li className="hidden md:block"><a href="#features" className="hover:text-brand-orange transition-colors">Features</a></li>
						<li className="hidden md:block"><a href="#numbers" className="hover:text-brand-orange transition-colors">Numbers</a></li>
						<li><a href={DOCS_URL} className="hover:text-brand-orange transition-colors">Docs</a></li>
						<li>
							<a href={GITHUB_URL} className="bg-brand-black text-white px-4 py-2 rounded italic hover:bg-brand-black/80 transition-all flex items-center gap-2">
								GitHub <ArrowRight aria-hidden="true" className="w-3 h-3" />
							</a>
						</li>
					</ul>
				</nav>
			</header>

			<main id="main" className="max-w-7xl mx-auto px-4 sm:px-6 pt-16 sm:pt-24">
				<section className="mb-24 sm:mb-32" aria-labelledby="hero-title">
					<div className="grid lg:grid-cols-12 gap-12 lg:gap-16 items-center">
						<div className="lg:col-span-7">
							<p className="reveal inline-flex items-center gap-2 px-3 py-1 rounded-full border border-brand-orange/40 text-brand-orange text-[11px] font-bold mb-8 uppercase tracking-[0.2em]">
								<Zap aria-hidden="true" className="w-3 h-3" /> In-memory event cache for .NET
							</p>
							<h1 id="hero-title" className="reveal reveal-2 text-5xl sm:text-7xl lg:text-8xl font-black tracking-tighter mb-8 leading-[0.9] italic uppercase">
								Prague<br />
								<span className="text-brand-orange">Caching</span>
							</h1>
							<p className="reveal reveal-3 text-lg sm:text-xl text-brand-black/75 max-w-xl leading-relaxed mb-10">
								Prague turns event-sourced Kafka topics into typed, indexed, queryable views inside your process.
								A Roslyn source generator does the wiring, so queries are checked at compile time and
								results are <strong className="text-brand-black">pooled with zero allocations</strong>.
							</p>
							<div className="flex flex-wrap gap-4">
								<a
									href={DOCS_URL}
									className="px-8 py-4 bg-brand-black text-white font-bold rounded-lg hover:scale-105 transition-all uppercase tracking-widest text-xs italic flex items-center gap-3 shadow-xl shadow-brand-black/20"
								>
									Get started <ChevronRight aria-hidden="true" className="w-4 h-4 text-brand-orange" />
								</a>
								<code className="px-5 py-4 bg-white border border-brand-black/10 rounded-lg flex items-center font-mono text-xs text-brand-black/80 whitespace-nowrap overflow-x-auto max-w-full">
									dotnet add package Prague
								</code>
							</div>
						</div>

						<div className="lg:col-span-5 relative sm:pb-10 sm:pr-10">
							<div className="aspect-square bg-white rounded-[2rem] sm:rounded-[3rem] border border-brand-black/5 shadow-2xl flex items-center justify-center relative overflow-hidden p-6 sm:p-8">
								<img
									src={`${BASE}logo-full.png`}
									alt="Prague logo: a line drawing of Franz Kafka beside the word Prague and a database icon"
									width="1408"
									height="768"
									className="w-full h-auto object-contain"
								/>
								<div className="absolute inset-0 border-[12px] sm:border-[20px] border-brand-black/5 pointer-events-none rounded-[2rem] sm:rounded-[3rem]" aria-hidden="true"></div>
							</div>
							<p className="mt-4 sm:mt-0 sm:absolute sm:bottom-0 sm:right-0 bg-brand-orange text-white p-6 sm:p-8 rounded-3xl shadow-xl sm:rotate-3 inline-block">
								<span className="block text-[11px] font-mono uppercase tracking-[0.2em] mb-1">Concurrent read throughput</span>
								<span className="block text-4xl font-black tracking-tight leading-none italic">11M+</span>
								<span className="block text-sm font-bold mt-1">reads per second</span>
							</p>
						</div>
					</div>
				</section>

				<section className="mb-24 sm:mb-32" aria-labelledby="sample-title">
					<div className="flex items-center gap-4 mb-10">
						<div className="h-px w-12 bg-brand-black" aria-hidden="true"></div>
						<h2 id="sample-title" className="text-2xl sm:text-3xl font-bold uppercase italic tracking-tighter">Attributes in, queries out</h2>
					</div>
					<p className="max-w-3xl text-brand-black/75 leading-relaxed mb-8">
						Every <code className="font-mono text-sm">[DataCache]</code> type is <code className="font-mono text-sm">partial</code>.
						The generator emits the cache class, the index storage and a fluent builder whose methods are named after your
						indices. Joins come from foreign-key attributes, so the join surface is also generated.
					</p>
					<pre className="bg-brand-black text-white rounded-[1.5rem] sm:rounded-[2rem] p-6 sm:p-10 overflow-x-auto text-[13px] leading-relaxed font-mono shadow-2xl" tabIndex={0}>
						<code>{sample}</code>
					</pre>
				</section>

				<section id="features" className="mb-24 sm:mb-32 scroll-mt-24" aria-labelledby="features-title">
					<div className="flex items-center gap-4 mb-12 sm:mb-16">
						<div className="h-px w-12 bg-brand-black" aria-hidden="true"></div>
						<h2 id="features-title" className="text-2xl sm:text-3xl font-bold uppercase italic tracking-tighter">Core architecture</h2>
					</div>

					<ul className="grid md:grid-cols-3 gap-6 sm:gap-8 list-none m-0 p-0">
						{features.map(({ icon: Icon, title, body, dark, muted }, i) => (
							<li
								key={title}
								className={[
									"p-8 sm:p-12 rounded-[2rem] sm:rounded-[3rem]",
									i === 0 ? "md:col-span-2" : "",
									dark ? "bg-brand-black text-white" : muted ? "bg-[#F2F2F2]" : "bg-white border border-brand-black/10",
								].join(" ")}
							>
								<Icon aria-hidden="true" className={`w-10 h-10 mb-8 ${muted ? "text-brand-black/50" : "text-brand-orange"}`} />
								<h3 className="text-2xl sm:text-3xl font-bold italic mb-4 uppercase tracking-tighter">{title}</h3>
								<p className={`leading-relaxed ${dark ? "text-white/80" : "text-brand-black/75"} ${i === 0 ? "max-w-lg" : "text-sm"}`}>{body}</p>
							</li>
						))}
					</ul>
				</section>

				<section id="numbers" className="mb-24 sm:mb-32 scroll-mt-24" aria-labelledby="numbers-title">
					<div className="bg-brand-black rounded-[2rem] sm:rounded-[4rem] p-8 sm:p-16 text-white overflow-hidden">
						<h2 id="numbers-title" className="text-4xl sm:text-5xl font-black italic mb-10 sm:mb-16 uppercase tracking-[0.05em] text-center">
							The <span className="text-brand-orange">numbers</span>
						</h2>

						<dl className="grid sm:grid-cols-2 lg:grid-cols-4 gap-8 mb-14">
							{stats.map(({ value, label }) => (
								<div key={value} className="border-l-2 border-brand-orange pl-5">
									<dt className="text-3xl sm:text-4xl font-black italic tracking-tight leading-none mb-2">{value}</dt>
									<dd className="text-sm text-white/80 leading-snug">{label}</dd>
								</div>
							))}
						</dl>
						<p className="text-sm text-white/70 max-w-3xl mb-14">
							All figures are from an Apple M4 Pro and vary by hardware. Reads per second is the sum of result rows returned
							divided by wall-clock seconds on a pooled three-level join with 10 reader threads and concurrent writers.
							Every measured table lives in <a href={RESULTS_URL} className="text-brand-orange underline underline-offset-4">RESULTS.MD</a>.
						</p>

						<div className="overflow-x-auto">
							<table className="w-full text-sm text-left border-collapse min-w-[40rem]">
								<caption className="text-left text-[11px] font-mono uppercase tracking-widest text-white/70 pb-4">
									How Prague differs from a remote cache and from MemoryCache
								</caption>
								<thead>
									<tr className="border-b border-white/20 text-[11px] font-mono uppercase tracking-wider text-white/70">
										<th scope="col" className="pb-4 pr-4 font-normal">Aspect</th>
										<th scope="col" className="pb-4 pr-4 text-brand-orange font-bold">Prague</th>
										<th scope="col" className="pb-4 pr-4 font-normal">Redis</th>
										<th scope="col" className="pb-4 font-normal">MemoryCache</th>
									</tr>
								</thead>
								<tbody>
									{comparison.map((row) => (
										<tr key={row.feature} className="border-b border-white/10 align-top">
											<th scope="row" className="py-4 pr-4 font-mono text-[11px] uppercase tracking-wider text-white/80 font-normal">{row.feature}</th>
											<td className="py-4 pr-4 text-brand-orange font-bold">{row.prague}</td>
											<td className="py-4 pr-4 text-white/85">{row.redis}</td>
											<td className="py-4 text-white/85">{row.memoryCache}</td>
										</tr>
									))}
								</tbody>
							</table>
						</div>
					</div>
				</section>

				<section className="mb-24 sm:mb-32" aria-labelledby="scope-title">
					<div className="grid md:grid-cols-2 gap-6 sm:gap-8">
						<div className="p-8 sm:p-12 bg-white border border-brand-black/10 rounded-[2rem] sm:rounded-[3rem]">
							<h2 id="scope-title" className="text-2xl font-bold italic uppercase tracking-tighter mb-6">What Prague is</h2>
							<ul className="space-y-4 text-brand-black/80 leading-relaxed">
								<li><strong className="text-brand-black">A library, not a server.</strong> Caches live in your process. There is no network hop.</li>
								<li><strong className="text-brand-black">Generated, not reflected.</strong> Storage, indices, joins and builders come from attributes; the plan is fixed by your call shape at compile time.</li>
								<li><strong className="text-brand-black">Pooled.</strong> Result sets rent from ArrayPool and return on Dispose.</li>
							</ul>
						</div>
						<div className="p-8 sm:p-12 bg-[#F2F2F2] rounded-[2rem] sm:rounded-[3rem]">
							<h2 className="text-2xl font-bold italic uppercase tracking-tighter mb-6">What Prague is not</h2>
							<ul className="space-y-4 text-brand-black/80 leading-relaxed">
								<li><strong className="text-brand-black">Not a distributed cache.</strong> Each process owns its caches; replication is the upstream log's job.</li>
								<li><strong className="text-brand-black">Not a relational database.</strong> Fluent expressions over typed indices, not SQL.</li>
								<li><strong className="text-brand-black">Not an eviction cache.</strong> No TTL or LRU. The topic's compaction is the retention policy.</li>
							</ul>
						</div>
					</div>
				</section>

				<section className="mb-24 sm:mb-32" aria-labelledby="packages-title">
					<div className="flex items-center gap-4 mb-10">
						<div className="h-px w-12 bg-brand-black" aria-hidden="true"></div>
						<h2 id="packages-title" className="text-2xl sm:text-3xl font-bold uppercase italic tracking-tighter">Packages</h2>
					</div>
					<pre className="bg-white border border-brand-black/10 rounded-[1.5rem] sm:rounded-[2rem] p-6 sm:p-8 overflow-x-auto text-[13px] leading-relaxed font-mono" tabIndex={0}>
						<code>{`dotnet add package Prague          # facade: Core + Attributes + Codegen
dotnet add package Prague.Kafka    # consumer/producer, filters, health checks, OpenTelemetry
dotnet add package Prague.Api      # optional: HTTP inspection endpoints
dotnet add package Prague.Api.UI   # optional: Blazor UI over Prague.Api`}</code>
					</pre>
					<p className="mt-6 text-brand-black/75 max-w-3xl leading-relaxed">
						Every package targets <code className="font-mono text-sm">net9.0</code>, <code className="font-mono text-sm">net10.0</code> and <code className="font-mono text-sm">net11.0</code>.
						Published versions are on <a href={NUGET_URL} className="font-bold underline underline-offset-4 decoration-brand-orange decoration-2">NuGet</a>.
						MIT licensed.
					</p>
				</section>

				<section className="text-center py-16 sm:py-24" aria-labelledby="cta-title">
					<h2 id="cta-title" className="text-5xl sm:text-7xl font-black tracking-tighter italic uppercase mb-8">
						Fast as <span className="text-brand-orange">thought</span>.
					</h2>
					<p className="text-lg sm:text-xl text-brand-black/70 max-w-xl mx-auto mb-12">
						Built on modern .NET for real-time systems that read far more than they write.
					</p>
					<a
						href={DOCS_URL}
						className="group px-8 sm:px-12 py-5 sm:py-6 bg-brand-black text-white rounded-full font-bold uppercase tracking-widest italic inline-flex items-center gap-4 mx-auto hover:bg-brand-orange transition-all duration-500 shadow-2xl"
					>
						Read the documentation <ArrowRight aria-hidden="true" className="w-5 h-5 group-hover:translate-x-2 transition-transform" />
					</a>
				</section>
			</main>

			<footer className="mt-24 sm:mt-40 border-t border-brand-black/5 pt-16 sm:pt-20 pb-12">
				<div className="max-w-7xl mx-auto px-4 sm:px-6 grid sm:grid-cols-2 md:grid-cols-4 gap-12">
					<div className="md:col-span-2">
						<div className="flex items-center gap-3 mb-6">
							<span className="w-8 h-8 bg-brand-black rounded-lg overflow-hidden flex items-center justify-center p-1 shadow-md">
								<img src={`${BASE}logo-mark.png`} alt="" width="1408" height="768" className="w-full h-full object-contain invert" />
							</span>
							<span className="text-xs font-bold uppercase italic tracking-widest underline decoration-brand-orange decoration-4">Prague</span>
						</div>
						<p className="text-[11px] uppercase font-mono text-brand-black/70 tracking-widest max-w-[280px] italic leading-relaxed">
							Named after the birthplace of Franz Kafka, whose works gave the Kafka streaming platform its name.
						</p>
					</div>
					<nav aria-labelledby="footer-resources" className="space-y-4">
						<h2 id="footer-resources" className="font-bold text-[11px] uppercase tracking-[0.2em] text-brand-black/70 italic">Resources</h2>
						<ul className="text-xs font-bold uppercase space-y-2 text-brand-black/80">
							<li><a href={DOCS_URL} className="hover:text-brand-orange transition-colors">Documentation</a></li>
							<li><a href={API_URL} className="hover:text-brand-orange transition-colors">API reference</a></li>
							<li><a href={NUGET_URL} className="hover:text-brand-orange transition-colors">NuGet</a></li>
							<li><a href={`${BASE}llms.txt`} className="hover:text-brand-orange transition-colors">llms.txt</a></li>
						</ul>
					</nav>
					<nav aria-labelledby="footer-community" className="space-y-4">
						<h2 id="footer-community" className="font-bold text-[11px] uppercase tracking-[0.2em] text-brand-black/70 italic">Project</h2>
						<ul className="text-xs font-bold uppercase space-y-2 text-brand-black/80">
							<li><a href={GITHUB_URL} className="hover:text-brand-orange transition-colors inline-flex items-center gap-2"><Github aria-hidden="true" className="w-3.5 h-3.5" /> GitHub</a></li>
							<li><a href={`${GITHUB_URL}/issues`} className="hover:text-brand-orange transition-colors">Issues</a></li>
							<li><a href={`${GITHUB_URL}#-contributing`} className="hover:text-brand-orange transition-colors">Contributing</a></li>
							<li><a href={`${GITHUB_URL}/blob/main/LICENSE`} className="hover:text-brand-orange transition-colors">MIT license</a></li>
						</ul>
					</nav>
				</div>
				<div className="max-w-7xl mx-auto px-4 sm:px-6 mt-16 sm:mt-20 pt-8 border-t border-brand-black/5 flex flex-wrap gap-4 justify-between items-center text-brand-black/70 text-[11px] font-mono uppercase tracking-widest italic">
					<p>© 2026 Prague contributors</p>
					<p>Built by systems engineers for scale</p>
				</div>
			</footer>
		</div>
	);
}
