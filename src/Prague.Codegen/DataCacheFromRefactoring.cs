namespace Prague.Codegen;

using System.Collections.Immutable;
using System.Composition;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
///   Analyzer that detects [DataCacheFrom&lt;T&gt;] and reports diagnostics when:
///   1. No hash is present (needs initial generation)
///   2. Hash doesn't match current source (source changed)
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DataCacheFromAnalyzer : DiagnosticAnalyzer {
	public const string DiagnosticIdNoHash = "DATACACHE001";
	public const string DiagnosticIdHashMismatch = "DATACACHE002";

	private static readonly DiagnosticDescriptor RuleNoHash = new(
		DiagnosticIdNoHash,
		"Generate properties from source type",
		"Class '{0}' has [DataCacheFrom<{1}>] but properties have not been generated yet",
		"CodeGeneration",
		DiagnosticSeverity.Info,
		true,
		"Use the code fix to generate properties from the source type.");

	private static readonly DiagnosticDescriptor RuleHashMismatch = new(
		DiagnosticIdHashMismatch,
		"Source type has changed",
		"Source type '{1}' has changed since properties were generated for '{0}'",
		"CodeGeneration",
		DiagnosticSeverity.Warning,
		true,
		"The source type has been modified. Use the code fix to regenerate properties.");

	public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
		ImmutableArray.Create(RuleNoHash, RuleHashMismatch);

	public override void Initialize(AnalysisContext context) {
		context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
		context.EnableConcurrentExecution();
		context.RegisterSymbolAction(AnalyzeClass, SymbolKind.NamedType);
	}

	private static void AnalyzeClass(SymbolAnalysisContext context) {
		var classSymbol = (INamedTypeSymbol)context.Symbol;

		// Find [DataCacheFrom<T>] attribute
		var attr = classSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.Name == "DataCacheFromAttribute"
			                     && a.AttributeClass.IsGenericType);

		if (attr?.AttributeClass is null) return;

		// Get the source type from the generic argument
		var sourceType = attr.AttributeClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
		if (sourceType is null) return;

		// Get hash from constructor argument (if present)
		string? storedHash = null;
		if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string hash) storedHash = hash;

		// Compute current hash of source type
		var currentHash = ComputeSourceHash(sourceType);

		// Get the attribute syntax location for the diagnostic
		var location = attr.ApplicationSyntaxReference?.GetSyntax().GetLocation()
		               ?? classSymbol.Locations.FirstOrDefault()
		               ?? Location.None;

		if (string.IsNullOrEmpty(storedHash))
			// No hash - needs initial generation
			context.ReportDiagnostic(Diagnostic.Create(
				RuleNoHash,
				location,
				classSymbol.Name,
				sourceType.Name));
		else if (!string.Equals(storedHash, currentHash, StringComparison.OrdinalIgnoreCase))
			// Hash mismatch - source changed
			context.ReportDiagnostic(Diagnostic.Create(
				RuleHashMismatch,
				location,
				classSymbol.Name,
				sourceType.Name));
	}

	/// <summary>
	///   Computes a 128-bit hash of the source type's properties and their attributes.
	/// </summary>
	internal static string ComputeSourceHash(INamedTypeSymbol sourceType) {
		var sb = new StringBuilder();

		// Include all public instance properties in deterministic order
		var properties = sourceType.GetMembers()
			.OfType<IPropertySymbol>()
			.Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
			.OrderBy(p => p.Name);

		foreach (var prop in properties) {
			sb.Append(prop.Name);
			sb.Append(':');
			sb.Append(prop.Type.ToDisplayString());
			sb.Append(';');

			// Include attributes
			foreach (var attrData in prop.GetAttributes().OrderBy(a => a.AttributeClass?.Name)) {
				sb.Append('[');
				sb.Append(attrData.AttributeClass?.ToDisplayString() ?? "?");

				// Constructor args
				foreach (var arg in attrData.ConstructorArguments) {
					sb.Append(',');
					sb.Append(arg.Value?.ToString() ?? "null");
				}

				// Named args
				foreach (var arg in attrData.NamedArguments.OrderBy(a => a.Key)) {
					sb.Append(',');
					sb.Append(arg.Key);
					sb.Append('=');
					sb.Append(arg.Value.Value?.ToString() ?? "null");
				}

				sb.Append(']');
			}

			sb.Append('|');
		}

		// Compute MD5 hash (128-bit) and return as GUID string
		using var md5 = MD5.Create();
		var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));

		return new Guid(hashBytes).ToString("D");
	}
}

/// <summary>
///   Code fix provider that generates properties from the source type.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DataCacheFromCodeFixProvider))]
[Shared]
public sealed class DataCacheFromCodeFixProvider : CodeFixProvider {
	/// <summary>
	///   Regex to find commented-out property declarations.
	///   Matches patterns like: // public string Name { get; set; }
	/// </summary>
	private static readonly Regex CommentedPropertyRegex = new(
		@"//\s*public\s+\S+\s+(\w+)\s*\{",
		RegexOptions.Compiled);

	public override ImmutableArray<string> FixableDiagnosticIds =>
		ImmutableArray.Create(DataCacheFromAnalyzer.DiagnosticIdNoHash, DataCacheFromAnalyzer.DiagnosticIdHashMismatch);

	public override FixAllProvider GetFixAllProvider() {
		return WellKnownFixAllProviders.BatchFixer;
	}

	public override async Task RegisterCodeFixesAsync(CodeFixContext context) {
		var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
		if (root is null) return;

		var diagnostic = context.Diagnostics.First();
		var diagnosticSpan = diagnostic.Location.SourceSpan;

		// Find the attribute syntax
		var node = root.FindNode(diagnosticSpan);
		var attrSyntax = node.AncestorsAndSelf().OfType<AttributeSyntax>().FirstOrDefault();
		if (attrSyntax is null) return;

		var classDecl = attrSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
		if (classDecl is null) return;

		var title = diagnostic.Id == DataCacheFromAnalyzer.DiagnosticIdNoHash
			? "Generate properties from source type"
			: "Regenerate properties (source changed)";

		context.RegisterCodeFix(
			CodeAction.Create(
				title,
				ct => GeneratePropertiesAsync(context.Document, classDecl, attrSyntax, ct),
				title),
			diagnostic);
	}

	/// <summary>
	///   Finds property names that have been commented out in the class.
	/// </summary>
	private static HashSet<string> FindCommentedOutProperties(ClassDeclarationSyntax classDecl) {
		var result = new HashSet<string>();

		// Get all trivia in the class
		foreach (var trivia in classDecl.DescendantTrivia())
			if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)) {
				var commentText = trivia.ToString();
				var match = CommentedPropertyRegex.Match(commentText);
				if (match.Success) result.Add(match.Groups[1].Value);
			}
			else if (trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)) {
				var commentText = trivia.ToString();
				// For multi-line, find all matches
				var matches = CommentedPropertyRegex.Matches(commentText);
				foreach (Match match in matches) result.Add(match.Groups[1].Value);
			}

		return result;
	}

	private static async Task<Document> GeneratePropertiesAsync(
		Document document,
		ClassDeclarationSyntax classDecl,
		AttributeSyntax attrSyntax,
		CancellationToken ct) {
		var semanticModel = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
		if (semanticModel is null) return document;

		var classSymbol = semanticModel.GetDeclaredSymbol(classDecl, ct);
		if (classSymbol is null) return document;

		// Find the DataCacheFrom attribute
		var attr = classSymbol.GetAttributes()
			.FirstOrDefault(a => a.AttributeClass?.Name == "DataCacheFromAttribute"
			                     && a.AttributeClass.IsGenericType);

		if (attr?.AttributeClass is null) return document;

		var sourceType = attr.AttributeClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
		if (sourceType is null) return document;

		// Get exclude/only lists from attribute
		var exclude = GetStringArrayFromAttribute(attr, "Exclude");
		var only = GetStringArrayFromAttribute(attr, "Only");
		var copyAttributes = GetBoolFromAttribute(attr, "CopyAttributes", true);

		// Get existing properties
		var existingProperties = new HashSet<string>(
			classDecl.Members.OfType<PropertyDeclarationSyntax>()
				.Select(p => p.Identifier.Text));

		// Find commented-out properties (user doesn't want them regenerated)
		var commentedOutProperties = FindCommentedOutProperties(classDecl);

		// Get properties from source type
		var sourceProperties = sourceType.GetMembers()
			.OfType<IPropertySymbol>()
			.Where(p => p.DeclaredAccessibility == Accessibility.Public && !p.IsStatic)
			.Where(p => !existingProperties.Contains(p.Name)) // Skip existing properties
			.Where(p => !commentedOutProperties.Contains(p.Name)) // Skip commented-out properties
			.Where(p => only is null || only.Contains(p.Name)) // Only filter
			.Where(p => exclude is null || !exclude.Contains(p.Name)) // Exclude filter
			.ToList();

		// Compute hash
		var sourceHash = DataCacheFromAnalyzer.ComputeSourceHash(sourceType);

		// Generate property declarations
		var propertyDeclarations = new List<MemberDeclarationSyntax>();
		foreach (var prop in sourceProperties) {
			var propSyntax = GenerateProperty(prop, copyAttributes);
			propertyDeclarations.Add(propSyntax);
		}

		// Update the class with new properties
		var newClassDecl = classDecl.AddMembers(propertyDeclarations.ToArray());

		// Update the attribute with the hash
		var newAttrSyntax = UpdateAttributeWithHash(attrSyntax, sourceHash);
		newClassDecl = newClassDecl.ReplaceNode(
			newClassDecl.DescendantNodes().OfType<AttributeSyntax>()
				.First(a => a.Name.ToString() == attrSyntax.Name.ToString()),
			newAttrSyntax);

		var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
		if (root is null) return document;

		var newRoot = root.ReplaceNode(classDecl, newClassDecl);
		return document.WithSyntaxRoot(newRoot);
	}

	private static HashSet<string>? GetStringArrayFromAttribute(AttributeData attr, string name) {
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key is null || arg.Value.IsNull) return null;

		if (arg.Value.Kind == TypedConstantKind.Array)
			return new HashSet<string>(
				arg.Value.Values
					.Select(v => v.Value as string)
					.Where(s => s is not null)!);

		return null;
	}

	private static bool GetBoolFromAttribute(AttributeData attr, string name, bool defaultValue) {
		var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
		if (arg.Key is null || arg.Value.IsNull) return defaultValue;
		return arg.Value.Value is bool b ? b : defaultValue;
	}

	private static PropertyDeclarationSyntax GenerateProperty(IPropertySymbol prop, bool copyAttributes) {
		var typeName = prop.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

		var propertySyntax = SyntaxFactory.PropertyDeclaration(
				SyntaxFactory.ParseTypeName(typeName),
				prop.Name)
			.AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
			.AddAccessorListAccessors(
				SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
					.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)),
				SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
					.WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)));

		if (copyAttributes) {
			var attributes = BuildAttributeList(prop.GetAttributes());
			if (attributes.Count > 0)
				propertySyntax = propertySyntax.AddAttributeLists(
					SyntaxFactory.AttributeList(SyntaxFactory.SeparatedList(attributes)));
		}

		return propertySyntax;
	}

	private static List<AttributeSyntax> BuildAttributeList(ImmutableArray<AttributeData> attributes) {
		var result = new List<AttributeSyntax>();

		foreach (var attr in attributes) {
			if (attr.AttributeClass is null) continue;

			// Skip compiler-generated attributes
			var name = attr.AttributeClass.Name;
			if (name.StartsWith("Compiler") || name.StartsWith("Debugger")) continue;

			// Get attribute name (remove "Attribute" suffix)
			var displayName = attr.AttributeClass.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
			if (displayName.EndsWith("Attribute"))
				displayName = displayName.Substring(0, displayName.Length - 9);

			var attrSyntax = SyntaxFactory.Attribute(SyntaxFactory.ParseName(displayName));

			var args = new List<AttributeArgumentSyntax>();

			// Constructor arguments
			foreach (var arg in attr.ConstructorArguments) {
				var expr = GetArgumentExpression(arg);
				if (expr is not null)
					args.Add(SyntaxFactory.AttributeArgument(expr));
			}

			// Named arguments
			foreach (var arg in attr.NamedArguments) {
				var expr = GetArgumentExpression(arg.Value);
				if (expr is not null)
					args.Add(SyntaxFactory.AttributeArgument(
						SyntaxFactory.NameEquals(arg.Key),
						null,
						expr));
			}

			if (args.Count > 0)
				attrSyntax = attrSyntax.WithArgumentList(
					SyntaxFactory.AttributeArgumentList(SyntaxFactory.SeparatedList(args)));

			result.Add(attrSyntax);
		}

		return result;
	}

	private static ExpressionSyntax? GetArgumentExpression(TypedConstant arg) {
		if (arg.IsNull)
			return SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);

		return arg.Kind switch {
			TypedConstantKind.Primitive => arg.Value switch {
				string s => SyntaxFactory.LiteralExpression(
					SyntaxKind.StringLiteralExpression,
					SyntaxFactory.Literal(s)),
				int i => SyntaxFactory.LiteralExpression(
					SyntaxKind.NumericLiteralExpression,
					SyntaxFactory.Literal(i)),
				long l => SyntaxFactory.LiteralExpression(
					SyntaxKind.NumericLiteralExpression,
					SyntaxFactory.Literal(l)),
				bool b => SyntaxFactory.LiteralExpression(
					b ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression),
				double d => SyntaxFactory.LiteralExpression(
					SyntaxKind.NumericLiteralExpression,
					SyntaxFactory.Literal(d)),
				float f => SyntaxFactory.LiteralExpression(
					SyntaxKind.NumericLiteralExpression,
					SyntaxFactory.Literal(f)),
				_ => null
			},

			TypedConstantKind.Enum => SyntaxFactory.MemberAccessExpression(
				SyntaxKind.SimpleMemberAccessExpression,
				SyntaxFactory.ParseTypeName(arg.Type!.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
				SyntaxFactory.IdentifierName(arg.Value!.ToString()!)),

			TypedConstantKind.Type when arg.Value is INamedTypeSymbol type =>
				SyntaxFactory.TypeOfExpression(
					SyntaxFactory.ParseTypeName(type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))),

			_ => null
		};
	}

	private static AttributeSyntax UpdateAttributeWithHash(AttributeSyntax attrSyntax, string sourceHash) {
		// Create new argument list with GUID hash
		var args = SyntaxFactory.AttributeArgumentList(
			SyntaxFactory.SeparatedList(new[] {
				SyntaxFactory.AttributeArgument(
					SyntaxFactory.LiteralExpression(
						SyntaxKind.StringLiteralExpression,
						SyntaxFactory.Literal(sourceHash)))
			}));

		return attrSyntax.WithArgumentList(args);
	}
}