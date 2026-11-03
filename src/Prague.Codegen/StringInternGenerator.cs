namespace Prague.Codegen;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

/// <summary>
///   Emits the implementing part of every <c>partial</c> string property marked
///   <c>[DataCacheStringIntern]</c>. For <c>[DataCacheStringIntern(false)]</c> that part carries
///   <c>[MessagePackFormatter(typeof(PragueStringFormatter))]</c>, so MessagePack reads the property
///   without Prague's interning formatter. The attribute only goes on when Prague.Kafka is referenced.
/// </summary>
[Generator]
public sealed class StringInternGenerator : IIncrementalGenerator {
	private const string AttributeName = "Prague.Core.DataCacheStringInternAttribute";

	private static readonly DiagnosticDescriptor NotString = new(
		"CACHE052",
		"[DataCacheStringIntern] on a non-string property",
		"Property '{0}' is of type '{1}'; [DataCacheStringIntern] applies to string properties only",
		"CacheGenerator",
		DiagnosticSeverity.Error,
		true);

	private static readonly DiagnosticDescriptor NotPartial = new(
		"CACHE053",
		"[DataCacheStringIntern] requires a partial property",
		"Property '{0}' must be declared partial (C# 13+) to carry [DataCacheStringIntern]; Prague generates its implementing part",
		"CacheGenerator",
		DiagnosticSeverity.Error,
		true);

	public void Initialize(IncrementalGeneratorInitializationContext context) {
		var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
			AttributeName,
			static (node, _) => node is PropertyDeclarationSyntax,
			static (ctx, _) => Transform(ctx));

		var hasKafka = context.CompilationProvider.Select(static (c, _) =>
			new KafkaSymbols(
				c.GetTypeByMetadataName("Prague.Kafka.SerDe.PragueStringFormatter") is not null,
				c.GetTypeByMetadataName("MessagePack.IgnoreMemberAttribute") is not null));

		context.RegisterSourceOutput(targets.Combine(hasKafka), static (spc, source) => Emit(spc, source.Left, source.Right));
	}

	private static Target Transform(GeneratorAttributeSyntaxContext ctx) {
		var syntax = (PropertyDeclarationSyntax)ctx.TargetNode;
		var property = (IPropertySymbol)ctx.TargetSymbol;
		var args = ctx.Attributes[0].ConstructorArguments;
		var intern = args.Length != 1 || args[0].Value is not false;

		if (property.Type.SpecialType != SpecialType.System_String)
			return Target.Error(Diagnostic.Create(NotString, syntax.Identifier.GetLocation(), property.Name,
				property.Type.ToDisplayString()));

		if (!syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
			return Target.Error(Diagnostic.Create(NotPartial, syntax.Identifier.GetLocation(), property.Name));

		// Only the defining part (accessors without bodies) drives emission; the implementing part is ours.
		if (syntax.AccessorList is not { } accessors || accessors.Accessors.Any(a => a.Body is not null || a.ExpressionBody is not null))
			return Target.None;

		var type = property.ContainingType;
		var source = new StringBuilder();
		var fieldName = "__prague_" + property.Name;
		var nullable = property.NullableAnnotation == NullableAnnotation.Annotated;

		var openTypes = new Stack<INamedTypeSymbol>();
		for (var t = type; t is not null; t = t.ContainingType)
			openTypes.Push(t);

		var head = new StringBuilder();
		foreach (var t in openTypes) {
			head.Append("partial ").Append(TypeKeyword(t)).Append(' ').Append(t.Name);
			if (t.TypeParameters.Length > 0)
				head.Append('<').Append(string.Join(", ", t.TypeParameters.Select(p => p.Name))).Append('>');
			head.AppendLine(" {");
		}

		// Resolvers that serialize private members must skip the backing field. A [MessagePackObject] type
		// without AllowPrivate skips private members already, and attributing one there trips MsgPack015.
		var keyedPublicOnly = type.GetAttributes().Any(a =>
			a.AttributeClass?.ToDisplayString() == "MessagePack.MessagePackObjectAttribute"
			&& !a.NamedArguments.Any(n => n.Key == "AllowPrivate" && n.Value.Value is true));
		source.Append(keyedPublicOnly ? "\t" : "\t{IGNORE}").Append(property.IsStatic ? "private static string? " : "private string? ").Append(fieldName).AppendLine(";");
		source.AppendLine();
		source.Append("\t{ATTRIBUTE}").Append(syntax.Modifiers.ToString()).Append(nullable ? " string? " : " string ")
			.Append(property.Name).AppendLine(" {");
		foreach (var accessor in accessors.Accessors) {
			source.Append("\t\t");
			if (accessor.Modifiers.Count > 0)
				source.Append(accessor.Modifiers.ToString()).Append(' ');
			source.Append(accessor.Keyword.Text);
			source.Append(accessor.Keyword.IsKind(SyntaxKind.GetKeyword)
				? " => " + fieldName + (nullable ? ";" : "!;")
				: " => " + fieldName + " = value;");
			source.AppendLine();
		}
		source.AppendLine("\t}");

		var ns = type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString();
		var hintName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
			.Replace("global::", "")
			.Replace('<', '[').Replace('>', ']') + "." + property.Name + ".StringIntern.g.cs";

		return new Target(hintName, ns, head.ToString(), source.ToString(), openTypes.Count, intern, null);
	}

	private static void Emit(SourceProductionContext spc, Target target, KafkaSymbols kafka) {
		if (target.Diagnostic is not null) {
			spc.ReportDiagnostic(target.Diagnostic);
			return;
		}
		if (target.HintName is null)
			return;

		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/>");
		sb.AppendLine("#nullable enable");
		if (target.Namespace is not null)
			sb.Append("namespace ").Append(target.Namespace).AppendLine(";");
		sb.AppendLine();
		sb.Append(target.Head);

		var member = target.Member;
		member = member.Replace("{IGNORE}", kafka.HasMessagePack ? "[global::MessagePack.IgnoreMember]\n\t" : "");
		member = member.Replace("{ATTRIBUTE}", !target.Intern && kafka.HasPragueStringFormatter
			? "[global::MessagePack.MessagePackFormatter(typeof(global::Prague.Kafka.SerDe.PragueStringFormatter))]\n\t"
			: "");
		sb.Append(member);

		for (var i = 0; i < target.Depth; i++)
			sb.AppendLine("}");

		spc.AddSource(target.HintName, sb.ToString());
	}

	private static string TypeKeyword(INamedTypeSymbol type) => type switch {
		{ IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
		{ IsRecord: true } => "record",
		{ TypeKind: TypeKind.Struct } => "struct",
		{ TypeKind: TypeKind.Interface } => "interface",
		_ => "class"
	};

	private readonly struct KafkaSymbols(bool hasPragueStringFormatter, bool hasMessagePack) : IEquatable<KafkaSymbols> {
		public readonly bool HasPragueStringFormatter = hasPragueStringFormatter;
		public readonly bool HasMessagePack = hasMessagePack;

		public bool Equals(KafkaSymbols other)
			=> HasPragueStringFormatter == other.HasPragueStringFormatter && HasMessagePack == other.HasMessagePack;

		public override bool Equals(object? obj) => obj is KafkaSymbols other && Equals(other);

		public override int GetHashCode() => (HasPragueStringFormatter ? 1 : 0) | (HasMessagePack ? 2 : 0);
	}

	/// <summary>Value-equatable pipeline model, so unchanged properties are not re-emitted on every edit.</summary>
	private sealed class Target(
		string? hintName, string? ns, string head, string member, int depth, bool intern, Diagnostic? diagnostic)
		: IEquatable<Target> {
		public static readonly Target None = new(null, null, "", "", 0, true, null);

		public readonly string? HintName = hintName;
		public readonly string? Namespace = ns;
		public readonly string Head = head;
		public readonly string Member = member;
		public readonly int Depth = depth;
		public readonly bool Intern = intern;
		public readonly Diagnostic? Diagnostic = diagnostic;

		public static Target Error(Diagnostic diagnostic) => new(null, null, "", "", 0, true, diagnostic);

		public bool Equals(Target? other)
			=> other is not null
			   && HintName == other.HintName
			   && Namespace == other.Namespace
			   && Head == other.Head
			   && Member == other.Member
			   && Depth == other.Depth
			   && Intern == other.Intern
			   && Equals(Diagnostic, other.Diagnostic);

		public override bool Equals(object? obj) => Equals(obj as Target);

		public override int GetHashCode() => (HintName?.GetHashCode() ?? 0) ^ Member.GetHashCode();
	}
}
