namespace Prague.Generated.Tests.Kafka;

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using Prague.Codegen;

/// <summary>
///   Runs <see cref="StringInternGenerator"/> on in-memory sources: the CACHE052/CACHE053 diagnostics, and the
///   formatter attribute appearing only when Prague.Kafka is referenced.
/// </summary>
[TestFixture]
public class StringInternGeneratorTests {
	private const string Usings = "using Prague.Core;\nnamespace Sample;\n";

	[Test]
	public void NonStringProperty_ReportsCache052() {
		var (diagnostics, _) = Run(Usings + "public partial class M { [DataCacheStringIntern(false)] public int Count { get; set; } }");

		Assert.That(Ids(diagnostics), Is.EqualTo(new[] { "CACHE052" }));
	}

	[Test]
	public void OptOutOnNonPartialProperty_ReportsCache053() {
		var (diagnostics, _) = Run(Usings + "public class M { [DataCacheStringIntern(false)] public string Name { get; set; } = \"\"; }");

		Assert.That(Ids(diagnostics), Is.EqualTo(new[] { "CACHE053" }));
	}

	[Test]
	public void DefaultOnNonPartialProperty_IsANoOp() {
		var (diagnostics, compilation) = Run(Usings + "public class M { [DataCacheStringIntern] public string Name { get; set; } = \"\"; }");

		Assert.That(Ids(diagnostics), Is.Empty);
		Assert.That(compilation.SyntaxTrees.Count(), Is.EqualTo(1), "Nothing is generated for a non-partial default.");
	}

	[Test]
	public void OptOut_WithKafka_EmitsFormatterAndCompiles() {
		var (diagnostics, compilation) = Run(
			Usings + "public partial class Outer<T> { public partial class M { [DataCacheStringIntern(false)] public partial string? Name { get; private set; } } }");

		Assert.That(Ids(diagnostics), Is.Empty);
		var generated = compilation.SyntaxTrees.Last().ToString();
		Assert.That(generated, Does.Contain("[global::MessagePack.MessagePackFormatter(typeof(global::Prague.Kafka.SerDe.PragueStringFormatter))]"));
		Assert.That(generated, Does.Contain("private set => __prague_Name = value;"));
	}

	[Test]
	public void OptOut_WithoutKafka_EmitsPlainImplementation() {
		var (diagnostics, compilation) = Run(
			Usings + "public partial class M { [DataCacheStringIntern(false)] public partial string Name { get; set; } }",
			withKafka: false);

		Assert.That(Ids(diagnostics), Is.Empty);
		Assert.That(compilation.SyntaxTrees.Last().ToString(), Does.Not.Contain("MessagePack"));
	}

	/// <summary>Runs the generator, then returns generator diagnostics plus every compile error of the result.</summary>
	private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Compilation) Run(string source, bool withKafka = true) {
		// The test host's assembly list already holds Prague.Attributes, Prague.Kafka and MessagePack.
		var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
			.Split(Path.PathSeparator)
			.Where(path => withKafka || !IsKafkaStack(Path.GetFileName(path)))
			.Select(path => MetadataReference.CreateFromFile(path));

		var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
		var compilation = CSharpCompilation.Create("StringInternSample",
			[CSharpSyntaxTree.ParseText(source, parseOptions)],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

		CSharpGeneratorDriver.Create([new StringInternGenerator().AsSourceGenerator()], parseOptions: parseOptions)
			.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);

		var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
		return (generatorDiagnostics.AddRange(errors), output);
	}

	private static bool IsKafkaStack(string file)
		=> file.StartsWith("MessagePack", StringComparison.Ordinal) || file.StartsWith("Prague.Kafka", StringComparison.Ordinal);

	private static string[] Ids(ImmutableArray<Diagnostic> diagnostics) => diagnostics.Select(d => d.Id).ToArray();
}
