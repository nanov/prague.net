import { motion } from "motion/react";
import {
	Zap,
	ShieldCheck,
	Cpu,
	Network,
	Clock,
	ArrowRight,
	ChevronRight,
	Database,
} from "lucide-react";
import logoMark from "./assets/images/prague_logo_kafka_abstract_1779032882065.png";
import logoFull from "./assets/images/prague_original_style_logo_1779043531303.png";

const DOCS_URL = `${import.meta.env.BASE_URL}docs/`;
const GITHUB_URL = "https://github.com/nanov/prague.net";

export default function App() {
	return (
		<div className="min-h-screen grid-bg selection:bg-brand-orange selection:text-white pb-20">
			<nav className="border-b border-brand-black/10 bg-white/80 backdrop-blur-md sticky top-0 z-50">
				<div className="max-w-7xl mx-auto px-6 h-20 flex items-center justify-between">
					<div className="flex items-center gap-4">
						<a href={import.meta.env.BASE_URL} className="w-10 h-10 bg-brand-black rounded-lg overflow-hidden flex items-center justify-center p-1 border border-white/10 shadow-lg cursor-pointer hover:scale-110 transition-transform">
							<img
								src={logoMark}
								alt="Prague"
								className="w-full h-full object-contain invert"
							/>
						</a>
						<a href={import.meta.env.BASE_URL} className="text-left">
							<h1 className="text-2xl font-bold tracking-tighter uppercase italic">Prague</h1>
							<p className="text-[10px] font-mono uppercase tracking-widest opacity-50">High Performance .NET Caching</p>
						</a>
					</div>
					<div className="hidden md:flex items-center gap-8 text-[11px] font-bold uppercase tracking-widest">
						<a href="#features" className="hover:text-brand-orange transition-colors">Features</a>
						<a href="#benchmarks" className="hover:text-brand-orange transition-colors">Benchmarks</a>
						<a href={DOCS_URL} className="hover:text-brand-orange transition-colors">Docs</a>
						<a href={GITHUB_URL} className="bg-brand-black text-white px-4 py-2 rounded italic hover:bg-brand-black/80 transition-all flex items-center gap-2">
							GitHub <ArrowRight className="w-3 h-3" />
						</a>
					</div>
				</div>
			</nav>

			<main className="max-w-7xl mx-auto px-6 pt-24">
				<section className="mb-32">
					<div className="grid lg:grid-cols-12 gap-16 items-center">
						<div className="lg:col-span-7">
							<motion.div
								initial={{ opacity: 0, x: -20 }}
								animate={{ opacity: 1, x: 0 }}
								className="inline-flex items-center gap-2 px-3 py-1 rounded-full border border-brand-orange/30 text-brand-orange text-[10px] font-bold mb-8 uppercase tracking-[0.2em]"
							>
								<Zap className="w-3 h-3" /> Built for Kafka's City
							</motion.div>
							<motion.h2
								initial={{ opacity: 0, y: 20 }}
								animate={{ opacity: 1, y: 0 }}
								transition={{ delay: 0.1 }}
								className="text-8xl font-black tracking-tighter mb-8 leading-[0.85] italic uppercase"
							>
								Prague<br />
								<span className="text-brand-orange">Caching</span>
							</motion.h2>
							<motion.p
								initial={{ opacity: 0 }}
								animate={{ opacity: 1 }}
								transition={{ delay: 0.2 }}
								className="text-xl text-brand-black/60 max-w-xl leading-relaxed mb-10"
							>
								The high-performance event cache designed for event sourcing.
								Transforming raw Kafka streams into lightning-fast queryable data
								with <span className="text-brand-black font-bold">zero allocations</span> and <span className="text-brand-black font-bold">compile-time safety</span>.
							</motion.p>
							<div className="flex flex-wrap gap-4">
								<a
									href={DOCS_URL}
									className="px-8 py-4 bg-brand-black text-white font-bold rounded-lg hover:scale-105 transition-all uppercase tracking-widest text-xs italic flex items-center gap-3 shadow-xl shadow-brand-black/20"
								>
									Get Started <ChevronRight className="w-4 h-4 text-brand-orange" />
								</a>
								<div className="px-6 py-4 bg-white border border-brand-black/10 rounded-lg flex items-center gap-3">
									<span className="font-mono text-xs opacity-40">dotnet add package Prague</span>
								</div>
							</div>
						</div>

						<div className="lg:col-span-5 relative">
							<div className="aspect-square bg-white rounded-[3rem] border border-brand-black/5 shadow-2xl flex items-center justify-center relative overflow-hidden group p-8">
								<div className="text-center w-full">
									<img
										src={logoFull}
										alt="Prague"
										className="w-full h-auto object-contain transform group-hover:scale-105 transition-transform duration-700"
									/>
								</div>
								<div className="absolute inset-0 border-[20px] border-brand-black/5 pointer-events-none rounded-[3rem]"></div>
							</div>
							<div className="absolute -bottom-6 -right-6 bg-brand-orange text-white p-8 rounded-3xl shadow-xl transform rotate-3">
								<p className="text-[10px] font-mono uppercase tracking-[0.2em] mb-1 opacity-80">Concurrent Read Speed</p>
								<p className="text-4xl font-black tracking-tight leading-none italic">15.9M+<br /><span className="text-sm">Reads/Sec</span></p>
							</div>
						</div>
					</div>
				</section>

				<section id="features" className="mb-32">
					<div className="flex items-center gap-4 mb-16">
						<div className="h-[1px] w-12 bg-brand-black"></div>
						<h3 className="text-3xl font-bold uppercase italic tracking-tighter">Core Architecture</h3>
					</div>

					<div className="grid md:grid-cols-3 gap-8">
						<div className="md:col-span-2 p-12 bg-white border border-brand-black/10 rounded-[3rem] group">
							<Cpu className="w-10 h-10 text-brand-orange mb-8 group-hover:scale-110 transition-transform" />
							<h4 className="text-3xl font-bold italic mb-4 uppercase tracking-tighter">Extreme Performance</h4>
							<p className="text-brand-black/60 leading-relaxed mb-8 max-w-lg">
								Source generators for zero runtime overhead, span-based APIs, and stack allocation.
								Engineered for <span className="text-brand-black font-bold">90% less GC pressure</span> through conditional updates
								and deep structural equality detection.
							</p>
							<div className="grid grid-cols-2 gap-8 pt-8 border-t border-brand-black/5 font-mono text-[11px] uppercase opacity-60 italic">
								<div>&lt; 50ns Updates</div>
								<div>&lt; 100ns Lookups</div>
							</div>
						</div>

						<div className="p-12 bg-brand-black text-white rounded-[3rem] relative overflow-hidden group">
							<div className="relative z-10">
								<ShieldCheck className="w-10 h-10 text-brand-orange mb-8" />
								<h4 className="text-3xl font-bold italic mb-4 uppercase tracking-tighter">Safe Caching</h4>
								<p className="opacity-60 leading-relaxed">
									Indices validated at compile time. No reflection, no magic strings. Modern .NET 9 features
									ensure type-safe range builders and joins.
								</p>
							</div>
							<div className="absolute -bottom-10 -right-10 opacity-10 blur-xl">
								<ShieldCheck className="w-48 h-48" />
							</div>
						</div>

						<div className="p-12 bg-white border border-brand-black/10 rounded-[3rem] group">
							<Network className="w-10 h-10 text-brand-orange mb-8 group-hover:rotate-12 transition-transform" />
							<h4 className="text-2xl font-bold italic mb-4 uppercase tracking-tighter">Event Sourcing</h4>
							<p className="text-brand-black/60 text-sm leading-relaxed">
								Seamless Kafka integration with change detection. Built-in producer conditional dispatch
								avoids unnecessary network traffic.
							</p>
						</div>

						<div className="p-12 bg-white border border-brand-black/10 rounded-[3rem] group">
							<Database className="w-10 h-10 text-brand-orange mb-8 group-hover:scale-95 transition-transform" />
							<h4 className="text-2xl font-bold italic mb-4 uppercase tracking-tighter">Auto Indices</h4>
							<p className="text-brand-black/60 text-sm leading-relaxed">
								Unique, Many, and Range indices chosen automatically by an execution plan. Short-circuit
								intersection stops processing on empty sets.
							</p>
						</div>

						<div className="p-12 bg-[#F2F2F2] rounded-[3rem] flex flex-col justify-between group">
							<div className="flex justify-between items-start">
								<Clock className="w-10 h-10 opacity-20" />
								<div className="bg-brand-black text-white text-[8px] font-bold px-2 py-1 rounded">EXPERIMENTAL</div>
							</div>
							<div>
								<h4 className="text-2xl font-bold italic mb-2 uppercase tracking-tighter">Global Index</h4>
								<p className="text-brand-black/40 text-xs leading-relaxed">
									Track updates across multiple caches via shared global timestamps for incremental sync.
								</p>
							</div>
						</div>
					</div>
				</section>

				<section id="benchmarks" className="mb-32">
					<div className="bg-brand-black rounded-[4rem] p-16 text-white overflow-hidden relative">
						<h3 className="text-5xl font-black italic mb-16 uppercase tracking-[0.05em] text-center">
							The <span className="text-brand-orange">Comparison</span>
						</h3>

						<div className="overflow-x-auto">
							<table className="w-full font-mono text-[11px] uppercase tracking-wider text-left border-collapse">
								<thead>
									<tr className="border-b border-white/10 opacity-40">
										<th className="pb-6">Feature</th>
										<th className="pb-6 text-brand-orange font-bold uppercase tracking-widest underline decoration-brand-orange decoration-offset-4 decoration-4">Prague</th>
										<th className="pb-6">Redis</th>
										<th className="pb-6">MemoryCache</th>
									</tr>
								</thead>
								<tbody className="opacity-80">
									<tr className="border-b border-white/5">
										<td className="py-6">Latency (Read)</td>
										<td className="py-6 text-brand-orange font-bold font-sans text-lg">&lt; 100ns</td>
										<td className="py-6">~ 500µs</td>
										<td className="py-6">N/A</td>
									</tr>
									<tr className="border-b border-white/5">
										<td className="py-6">Type Safety</td>
										<td className="py-6 text-brand-orange font-bold font-sans text-sm">Compile-Time</td>
										<td className="py-6">Runtime</td>
										<td className="py-6">Limited</td>
									</tr>
									<tr className="border-b border-white/5">
										<td className="py-6">Allocations</td>
										<td className="py-6 text-brand-orange font-bold font-sans text-sm">Zero-Pooled</td>
										<td className="py-6">High</td>
										<td className="py-6">Per Object</td>
									</tr>
									<tr>
										<td className="py-6">Query Opt.</td>
										<td className="py-6 text-brand-orange font-bold font-sans text-sm">Automatic</td>
										<td className="py-6 font-sans text-xs">Manual (Lua)</td>
										<td className="py-6">None</td>
									</tr>
								</tbody>
							</table>
						</div>
					</div>
				</section>

				<section className="text-center py-24">
					<h2 className="text-7xl font-black tracking-tighter italic uppercase mb-8">
						Fast as <span className="text-brand-orange">Thought</span>.
					</h2>
					<p className="text-xl opacity-40 max-w-xl mx-auto mb-12">
						Built with modern .NET 9 features for the most demanding real-time systems in the world.
					</p>
					<a
						href={DOCS_URL}
						className="group px-12 py-6 bg-brand-black text-white rounded-full font-bold uppercase tracking-widest italic inline-flex items-center gap-4 mx-auto hover:bg-brand-orange transition-all duration-500 shadow-2xl"
					>
						Read the Documentation <ArrowRight className="w-5 h-5 group-hover:translate-x-2 transition-transform" />
					</a>
				</section>
			</main>

			<footer className="mt-40 border-t border-brand-black/5 pt-20 pb-12">
				<div className="max-w-7xl mx-auto px-6 grid md:grid-cols-4 gap-12">
					<div className="md:col-span-2">
						<div className="flex items-center gap-3 mb-6">
							<div className="w-8 h-8 bg-brand-black rounded-lg overflow-hidden flex items-center justify-center p-1 shadow-md">
								<img src={logoMark} alt="" className="w-full h-full object-contain invert" />
							</div>
							<h1 className="text-xs font-bold uppercase italic tracking-widest underline decoration-brand-orange decoration-4">PRAGUE</h1>
						</div>
						<p className="text-[10px] uppercase font-mono opacity-40 tracking-widest max-w-[240px] italic leading-relaxed">
							Named after the birthplace of Franz Kafka, whose works inspired the Kafka streaming platform. Performance without compromise.
						</p>
					</div>
					<div className="space-y-4">
						<h5 className="font-bold text-[10px] uppercase tracking-[0.2em] opacity-40 italic">Resources</h5>
						<ul className="text-xs font-bold uppercase space-y-2 opacity-60">
							<li><a href={GITHUB_URL} className="hover:text-brand-orange transition-colors">GitHub Repository</a></li>
							<li><a href={DOCS_URL} className="hover:text-brand-orange transition-colors">Documentation</a></li>
							<li><a href={`${DOCS_URL}api/`} className="hover:text-brand-orange transition-colors">API Reference</a></li>
						</ul>
					</div>
					<div className="space-y-4">
						<h5 className="font-bold text-[10px] uppercase tracking-[0.2em] opacity-40 italic">Community</h5>
						<ul className="text-xs font-bold uppercase space-y-2 opacity-60">
							<li><a href={`${GITHUB_URL}/discussions`} className="hover:text-brand-orange transition-colors">Discussions</a></li>
							<li><a href={`${GITHUB_URL}/issues`} className="hover:text-brand-orange transition-colors">Issues</a></li>
							<li><a href={`${GITHUB_URL}/blob/main/CONTRIBUTING.md`} className="hover:text-brand-orange transition-colors">Contributing</a></li>
						</ul>
					</div>
				</div>
				<div className="max-w-7xl mx-auto px-6 mt-20 pt-8 border-t border-brand-black/5 flex justify-between items-center opacity-30 text-[10px] font-mono uppercase tracking-widest italic">
					<p>© 2026 Prague. All rights reserved.</p>
					<p>Built by systems engineers for scale</p>
				</div>
			</footer>
		</div>
	);
}
