using System.Globalization;
using System.Text;

namespace Guanacaste3D.Core;

/// <summary>A node of a parsed parameter formula.</summary>
public abstract record FormulaNode;

/// <summary>
/// A numeric literal in metric base units. A unit suffix gives it a power of
/// length: "50 cm" is 0.5 with power 1, "1 m²" is 1 with power 2.
/// </summary>
public sealed record NumberNode(double Value, int LengthPower = 0) : FormulaNode;

/// <summary>A quoted text literal, usable as an <c>if</c> branch of a text formula.</summary>
public sealed record TextNode(string Value) : FormulaNode;

/// <summary>A reference to another parameter or to a built-in quantity by name.</summary>
public sealed record ReferenceNode(string Name) : FormulaNode;

/// <summary>A unary operator: negation or logical not.</summary>
public sealed record UnaryNode(string Operator, FormulaNode Operand) : FormulaNode;

/// <summary>A binary arithmetic, comparison, or logical operator.</summary>
public sealed record BinaryNode(string Operator, FormulaNode Left, FormulaNode Right) : FormulaNode;

/// <summary>A function call such as <c>if(a, b, c)</c> or <c>round(x)</c>.</summary>
public sealed record CallNode(string Function, IReadOnlyList<FormulaNode> Arguments) : FormulaNode;

/// <summary>
/// Parses parameter formulas. Grammar (lowest to highest precedence):
/// <c>or</c>, <c>and</c>, comparisons (= &lt;&gt; &lt; &lt;= &gt; &gt;=), + -, * /, ^, unary - and not.
/// References are bare identifiers (<c>Width</c>) or bracketed names with
/// spaces (<c>[Fire rating]</c>). Numbers always use '.' as decimal separator so
/// formulas mean the same in every UI language.
/// </summary>
public sealed class FormulaParser
{
    private const int MaximumLength = 2000;
    private const int MaximumDepth = 64;
    private readonly string _text;
    private int _position;
    private int _depth;

    private FormulaParser(string text) => _text = text;

    /// <summary>Parses a formula or throws <see cref="InvalidDataException"/> describing the first error.</summary>
    public static FormulaNode Parse(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > MaximumLength)
            throw new InvalidDataException("The formula is empty or too long.");
        var parser = new FormulaParser(formula);
        var node = parser.ParseOr();
        parser.SkipSpaces();
        if (parser._position != formula.Length)
            throw parser.Error("Unexpected text");
        return node;
    }

    /// <summary>Returns every reference name used by a formula, without duplicates.</summary>
    public static IReadOnlySet<string> References(FormulaNode node)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(FormulaNode current)
        {
            switch (current)
            {
                case ReferenceNode reference: names.Add(reference.Name); break;
                case UnaryNode unary: Visit(unary.Operand); break;
                case BinaryNode binary: Visit(binary.Left); Visit(binary.Right); break;
                case CallNode call: foreach (var argument in call.Arguments) Visit(argument); break;
            }
        }
        Visit(node);
        return names;
    }

    private FormulaNode ParseOr() => ParseLeft(ParseAnd, "or");
    private FormulaNode ParseAnd() => ParseLeft(ParseComparison, "and");

    /// <summary>Parses a left-associative chain of keyword operators.</summary>
    private FormulaNode ParseLeft(Func<FormulaNode> next, string keyword)
    {
        var left = next();
        while (TryKeyword(keyword)) left = new BinaryNode(keyword, left, next());
        return left;
    }

    /// <summary>Parses at most one comparison between two additive expressions.</summary>
    private FormulaNode ParseComparison()
    {
        var left = ParseAdditive();
        foreach (var op in new[] { "<=", ">=", "<>", "=", "<", ">" })
            if (TrySymbol(op)) return new BinaryNode(op, left, ParseAdditive());
        return left;
    }

    /// <summary>Parses + and - chains.</summary>
    private FormulaNode ParseAdditive()
    {
        var left = ParseMultiplicative();
        while (true)
        {
            if (TrySymbol("+")) left = new BinaryNode("+", left, ParseMultiplicative());
            else if (TrySymbol("-")) left = new BinaryNode("-", left, ParseMultiplicative());
            else return left;
        }
    }

    /// <summary>Parses * and / chains.</summary>
    private FormulaNode ParseMultiplicative()
    {
        var left = ParsePower();
        while (true)
        {
            if (TrySymbol("*")) left = new BinaryNode("*", left, ParsePower());
            else if (TrySymbol("/")) left = new BinaryNode("/", left, ParsePower());
            else return left;
        }
    }

    /// <summary>Parses right-associative exponentiation.</summary>
    private FormulaNode ParsePower()
    {
        var left = ParseUnary();
        return TrySymbol("^") ? new BinaryNode("^", left, ParsePower()) : left;
    }

    /// <summary>Parses unary minus and logical not with a recursion guard.</summary>
    private FormulaNode ParseUnary()
    {
        if (++_depth > MaximumDepth) throw Error("The formula is nested too deeply");
        try
        {
            if (TrySymbol("-")) return new UnaryNode("-", ParseUnary());
            if (TryKeyword("not")) return new UnaryNode("not", ParseUnary());
            return ParsePrimary();
        }
        finally { _depth--; }
    }

    /// <summary>Parses literals, references, calls, and parenthesized expressions.</summary>
    private FormulaNode ParsePrimary()
    {
        SkipSpaces();
        if (_position >= _text.Length) throw Error("Unexpected end of formula");
        var current = _text[_position];
        if (TrySymbol("("))
        {
            var inner = ParseOr();
            Expect(")");
            return inner;
        }
        if (char.IsDigit(current) || current == '.') return ParseNumber();
        if (current == '"') return ParseText();
        if (current == '[')
        {
            var end = _text.IndexOf(']', _position + 1);
            if (end < 0) throw Error("Missing ']'");
            var name = _text[(_position + 1)..end].Trim();
            if (name.Length == 0) throw Error("Empty reference");
            _position = end + 1;
            return new ReferenceNode(name);
        }
        if (char.IsLetter(current) || current == '_')
        {
            var start = _position;
            while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) || _text[_position] == '_'))
                _position++;
            var identifier = _text[start.._position];
            if (!TrySymbol("(")) return new ReferenceNode(identifier);
            var arguments = new List<FormulaNode>();
            if (!TrySymbol(")"))
            {
                do arguments.Add(ParseOr()); while (TrySymbol(","));
                Expect(")");
            }
            return new CallNode(identifier.ToLowerInvariant(), arguments);
        }
        throw Error($"Unexpected character '{current}'");
    }

    /// <summary>Parses an invariant-culture decimal number with optional exponent.</summary>
    private NumberNode ParseNumber()
    {
        var start = _position;
        while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
        if (_position < _text.Length && (_text[_position] is 'e' or 'E'))
        {
            _position++;
            if (_position < _text.Length && (_text[_position] is '+' or '-')) _position++;
            while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
        }
        if (!double.TryParse(_text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw Error("Invalid number");
        var afterNumber = _position;
        SkipSpaces();
        foreach (var (unit, scale, power) in Units)
        {
            var end = _position + unit.Length;
            if (end <= _text.Length && string.CompareOrdinal(_text, _position, unit, 0, unit.Length) == 0 &&
                (end == _text.Length || !(char.IsLetterOrDigit(_text[end]) || _text[end] is '_' or '²' or '³')))
            {
                _position = end;
                return new NumberNode(value * scale, power);
            }
        }
        _position = afterNumber;
        return new NumberNode(value);
    }

    /// <summary>Unit suffixes accepted after number literals, longest first so "mm" wins over "m".</summary>
    private static readonly (string Unit, double Scale, int Power)[] Units =
    [
        ("mm²", 0.000001, 2), ("cm²", 0.0001, 2), ("mm2", 0.000001, 2), ("cm2", 0.0001, 2),
        ("mm³", 1e-9, 3), ("cm³", 1e-6, 3), ("mm3", 1e-9, 3), ("cm3", 1e-6, 3),
        ("m²", 1, 2), ("m2", 1, 2), ("m³", 1, 3), ("m3", 1, 3),
        ("mm", 0.001, 1), ("cm", 0.01, 1), ("m", 1, 1)
    ];

    /// <summary>Parses a double-quoted text literal; "" inside the literal is an escaped quote.</summary>
    private TextNode ParseText()
    {
        var builder = new StringBuilder();
        _position++;
        while (_position < _text.Length)
        {
            if (_text[_position] == '"')
            {
                if (_position + 1 < _text.Length && _text[_position + 1] == '"') { builder.Append('"'); _position += 2; continue; }
                _position++;
                return new TextNode(builder.ToString());
            }
            builder.Append(_text[_position++]);
        }
        throw Error("Missing closing quote");
    }

    /// <summary>Consumes a symbol after optional spaces when present.</summary>
    private bool TrySymbol(string symbol)
    {
        SkipSpaces();
        if (string.CompareOrdinal(_text, _position, symbol, 0, symbol.Length) != 0) return false;
        _position += symbol.Length;
        return true;
    }

    /// <summary>Consumes a case-insensitive keyword that is not the prefix of a longer identifier.</summary>
    private bool TryKeyword(string keyword)
    {
        SkipSpaces();
        var end = _position + keyword.Length;
        if (end > _text.Length || string.Compare(_text, _position, keyword, 0, keyword.Length,
                StringComparison.OrdinalIgnoreCase) != 0 ||
            (end < _text.Length && (char.IsLetterOrDigit(_text[end]) || _text[end] == '_' || _text[end] == '(')))
            return false;
        _position = end;
        return true;
    }

    /// <summary>Requires a symbol or reports a positioned error.</summary>
    private void Expect(string symbol)
    {
        if (!TrySymbol(symbol)) throw Error($"Expected '{symbol}'");
    }

    /// <summary>Skips whitespace between tokens.</summary>
    private void SkipSpaces()
    {
        while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++;
    }

    /// <summary>Builds a parse error that includes the 1-based character position.</summary>
    private InvalidDataException Error(string message) =>
        new($"{message} at position {_position + 1} of the formula.");
}
