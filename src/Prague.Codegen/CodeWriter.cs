namespace Prague.Codegen;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

/// <summary>
/// Delegate for CodeWriter callbacks.
/// </summary>
internal delegate void CodeWriterAction(ref CodeWriter writer);

internal ref struct CodeWriter {
	private ValueStringBuilder _sb;
	private int _indent;
	private bool _atLineStart;
	private const string IndentString = "\t";

	public CodeWriter(Span<char> buffer) {
		_sb = new ValueStringBuilder(buffer);
		_indent = 0;
		_atLineStart = true;
	}

	public CodeWriter():this(4096) {
	}

	public CodeWriter(int initialCapacity) {
		_sb = new ValueStringBuilder(initialCapacity);
		_indent = 0;
		_atLineStart = true;
	}

	private void WriteIndentIfNeeded() {
		if (_atLineStart && _indent > 0) {
			for (var i = 0; i < _indent; i++)
				_sb.Append(IndentString);
		}

		_atLineStart = false;
	}

	public CodeWriter Line(string text)
		=> Line(text.AsSpan());

	/// <summary>
	/// Writes a line of code with the current indentation.
	/// </summary>
	public CodeWriter Line(ReadOnlySpan<char> text) {
		WriteIndentIfNeeded();
		_sb.Append(text);
		_sb.Append('\n');
		_atLineStart = true;
		return this;
	}

	/// <summary>
	/// Writes an empty line.
	/// </summary>
	public CodeWriter Line() {
		_sb.Append('\n');
		_atLineStart = true;
		return this;
	}

	/// <summary>
	/// Writes text without a newline.
	/// </summary>
	public CodeWriter Write(string text) {
		WriteIndentIfNeeded();
		_sb.Append(text);
		return this;
	}

	/// <summary>
	/// Opens a brace block on the same line as the previous content.
	/// Writes " {" and increases indentation.
	/// </summary>
	public CodeWriter OpenBrace() {
		_sb.Append(" {\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Writes a line and opens a brace block on the same line.
	/// Example: "public class Foo {"
	/// </summary>
	public CodeWriter OpenBrace(string text) {
		WriteIndentIfNeeded();
		_sb.Append(text);
		_sb.Append(" {\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Closes a brace block. Decreases indentation and writes "}".
	/// </summary>
	public CodeWriter CloseBrace() {
		_indent--;
		WriteIndentIfNeeded();
		_sb.Append("}\n");
		_atLineStart = true;
		return this;
	}

	/// <summary>
	/// Closes a brace block with additional text after the brace.
	/// Example: "};" or "});"
	/// </summary>
	public CodeWriter CloseBrace(string suffix) {
		_indent--;
		WriteIndentIfNeeded();
		_sb.Append('}');
		_sb.Append(suffix);
		_sb.Append('\n');
		_atLineStart = true;
		return this;
	}

	/// <summary>
	/// Writes a file-scoped namespace declaration (no braces, no indentation).
	/// Example: namespace MyNamespace;
	/// </summary>
	public CodeWriter Namespace(string namespaceName) => Line($"namespace {namespaceName};");

	/// <summary>
	/// Writes a block-scoped namespace with opening brace.
	/// Use when multiple namespaces are needed in the same file.
	/// </summary>
	public CodeWriter NamespaceBlock(string namespaceName) => OpenBrace($"namespace {namespaceName}");

	/// <summary>
	/// Writes a block-scoped namespace with body and automatic closing brace.
	/// Use when multiple namespaces are needed in the same file.
	/// </summary>
	public CodeWriter NamespaceBlock(string namespaceName, CodeWriterAction body) {
		NamespaceBlock(namespaceName);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes a class declaration with opening brace.
	/// Example: "public partial class MyClass" or "public class MyClass&lt;T&gt; : IBase where T : class"
	/// </summary>
	public CodeWriter Class(string declaration) => OpenBrace(declaration);

	/// <summary>
	/// Writes a class declaration with body and automatic closing brace.
	/// Example: w.Class("public partial class MyClass&lt;T&gt; : IBase where T : class", w => { ... })
	/// </summary>
	public CodeWriter Class(string declaration, CodeWriterAction body) {
		Class(declaration);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes a struct declaration with opening brace.
	/// Example: "public struct MyStruct" or "public readonly struct MyStruct&lt;T&gt; : IEquatable&lt;T&gt;"
	/// </summary>
	public CodeWriter Struct(string declaration) => OpenBrace(declaration);

	/// <summary>
	/// Writes a struct declaration with body and automatic closing brace.
	/// </summary>
	public CodeWriter Struct(string declaration, CodeWriterAction body) {
		Struct(declaration);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes a method declaration with opening brace.
	/// </summary>
	public CodeWriter Method(string signature) => OpenBrace(signature);

	/// <summary>
	/// Writes a method declaration with body and automatic closing brace.
	/// </summary>
	public CodeWriter Method(string signature, CodeWriterAction body) {
		Method(signature);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes an if statement with opening brace.
	/// </summary>
	public CodeWriter If(string condition) => OpenBrace($"if ({condition})");

	/// <summary>
	/// Writes an if statement with body and automatic closing brace.
	/// </summary>
	public CodeWriter If(string condition, CodeWriterAction body) {
		If(condition);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes an else block with opening brace.
	/// </summary>
	public CodeWriter Else() {
		_indent--;
		WriteIndentIfNeeded();
		_sb.Append("} else {\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Writes an else if block with opening brace.
	/// </summary>
	public CodeWriter ElseIf(string condition) {
		_indent--;
		WriteIndentIfNeeded();
		_sb.Append("} else if (");
		_sb.Append(condition);
		_sb.Append(") {\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Writes a foreach statement with opening brace.
	/// </summary>
	public CodeWriter ForEach(string declaration) => OpenBrace($"foreach ({declaration})");

	/// <summary>
	/// Writes a foreach statement with body and automatic closing brace.
	/// </summary>
	public CodeWriter ForEach(string declaration, CodeWriterAction body) {
		ForEach(declaration);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes a for statement with opening brace.
	/// </summary>
	public CodeWriter For(string declaration) => OpenBrace($"for ({declaration})");

	/// <summary>
	/// Writes a for statement with body and automatic closing brace.
	/// </summary>
	public CodeWriter For(string declaration, CodeWriterAction body) {
		For(declaration);
		body(ref this);
		CloseBrace();
		return this;
	}

	/// <summary>
	/// Writes a switch statement with opening brace.
	/// </summary>
	public CodeWriter Switch(string expression) => OpenBrace($"switch ({expression})");

	/// <summary>
	/// Writes a case label.
	/// </summary>
	public CodeWriter Case(string value) {
		WriteIndentIfNeeded();
		_sb.Append("case ");
		_sb.Append(value);
		_sb.Append(":\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Writes a default case label.
	/// </summary>
	public CodeWriter DefaultCase() {
		WriteIndentIfNeeded();
		_sb.Append("default:\n");
		_atLineStart = true;
		_indent++;
		return this;
	}

	/// <summary>
	/// Ends a case block (decreases indent).
	/// </summary>
	public CodeWriter EndCase() {
		_indent--;
		return this;
	}

	/// <summary>
	/// Writes an XML documentation summary.
	/// </summary>
	public CodeWriter Summary(string text) {
		Line("/// <summary>");
		Line($"/// {text}");
		Line("/// </summary>");
		return this;
	}

	/// <summary>
	/// Writes a multi-line XML documentation summary.
	/// </summary>
	public CodeWriter Summary(params string[] lines) {
		Line("/// <summary>");
		foreach (var line in lines)
			Line($"/// {line}");
		Line("/// </summary>");
		return this;
	}

	/// <summary>
	/// Writes an XML documentation param tag.
	/// </summary>
	public CodeWriter Param(string name, string description) => Line($"/// <param name=\"{name}\">{description}</param>");

	/// <summary>
	/// Writes an XML documentation returns tag.
	/// </summary>
	public CodeWriter Returns(string description) => Line($"/// <returns>{description}</returns>");

	/// <summary>
	/// Writes a pragma directive.
	/// </summary>
	public CodeWriter Pragma(string directive) => Line($"#pragma {directive}");

	/// <summary>
	/// Writes a using directive.
	/// </summary>
	public CodeWriter Using(string namespaceName) => Line($"using {namespaceName};");

	/// <summary>
	/// Writes an attribute.
	/// </summary>
	public CodeWriter Attribute(string attribute) => Line($"[{attribute}]");

	/// <summary>
	/// Increases the indentation level.
	/// </summary>
	public CodeWriter IncreaseIndent(int level = 1) {
		_indent += level;
		return this;
	}

	/// <summary>
	/// Executes the body with increased indentation, then restores the original level.
	/// </summary>
	public CodeWriter Indent(CodeWriterAction body) {
		_indent++;
		body(ref this);
		_indent--;
		return this;
	}

	/// <summary>
	/// Decreases the indentation level.
	/// </summary>
	public CodeWriter DecreaseIndent(int level = 1) {
		_indent = Math.Max(0, _indent - level);
		return this;
	}

	/// <summary>
	/// Writes a multi-line block of code, applying current indentation to each line.
	/// Useful for raw string literals containing multiple lines of code.
	/// </summary>
	public CodeWriter Block(string multiLineText) {
		var lines = multiLineText.Split(["\r\n", "\n"], StringSplitOptions.None);
		foreach (var line in lines)
			Line(line);
		return this;
	}

	/// <summary>
	/// Writes a method/property signature followed by an expression body on the next line.
	/// Handles indentation automatically.
	/// Example: "public bool TryGetMin(...)" + "=> Index.TryGetMin(...);"
	/// </summary>
	public CodeWriter ExpressionBody(string signature, string expression) {
		Line(signature);
		_indent++;
		Line($"=> {expression};");
		_indent--;
		return this;
	}

	/// <summary>
	/// Returns the generated code as a string and disposes the underlying buffer.
	/// </summary>
	public override string ToString() => _sb.ToString();

	/// <summary>
	/// Disposes the underlying buffer without returning a string.
	/// </summary>
	public void Dispose() => _sb.Dispose();

	private ref struct ValueStringBuilder {
		private char[]? _arrayToReturnToPool;
		private Span<char> _chars;
		private int _pos;

		public ValueStringBuilder(Span<char> initialBuffer) {
			_arrayToReturnToPool = null;
			_chars = initialBuffer;
			_pos = 0;
		}

		public ValueStringBuilder(int initialCapacity) {
			_arrayToReturnToPool = ArrayPool<char>.Shared.Rent(initialCapacity);
			_chars = _arrayToReturnToPool;
			_pos = 0;
		}

		public override string ToString() {
			var s = _chars.Slice(0, _pos).ToString();
			Dispose();
			return s;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Append(char c) {
			var pos = _pos;
			if ((uint)pos < (uint)_chars.Length) {
				_chars[pos] = c;
				_pos = pos + 1;
			} else {
				GrowAndAppend(c);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Append(string? s) {
			if (s == null) {
				return;
			}

			var pos = _pos;
			if (s.Length == 1 && (uint)pos < (uint)_chars.Length) {
				_chars[pos] = s[0];
				_pos = pos + 1;
			} else {
				AppendSlow(s);
			}
		}

		private void AppendSlow(string s) {
			var pos = _pos;
			if (pos > _chars.Length - s.Length) {
				Grow(s.Length);
			}

			s.AsSpan().CopyTo(_chars.Slice(pos));
			_pos += s.Length;
		}


		public void Append(ReadOnlySpan<char> value) {
			var pos = _pos;
			if (pos > _chars.Length - value.Length) {
				Grow(value.Length);
			}

			value.CopyTo(_chars.Slice(_pos));
			_pos += value.Length;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private void GrowAndAppend(char c) {
			Grow(1);
			Append(c);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private void Grow(int additionalCapacityBeyondPos) {
			Debug.Assert(additionalCapacityBeyondPos > 0);
			Debug.Assert(_pos > _chars.Length - additionalCapacityBeyondPos,
				"Grow called incorrectly, no resize is needed.");

			var poolArray = ArrayPool<char>.Shared.Rent((int)Math.Max((uint)(_pos + additionalCapacityBeyondPos),
				(uint)_chars.Length * 2));

			_chars.Slice(0, _pos).CopyTo(poolArray);

			var toReturn = _arrayToReturnToPool;
			_chars = _arrayToReturnToPool = poolArray;
			if (toReturn != null) {
				ArrayPool<char>.Shared.Return(toReturn);
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Dispose() {
			var toReturn = _arrayToReturnToPool;
			this = default;
			if (toReturn != null) {
				ArrayPool<char>.Shared.Return(toReturn);
			}
		}
	}
}
