namespace Guanacaste3D.Core;

/// <summary>The value category of a formula expression.</summary>
public enum FormulaKind
{
    Number,
    Boolean,
    Text
}

/// <summary>
/// The static type of a formula expression. Numbers carry the power of length
/// they represent (0 = plain number or angle, 1 = length, 2 = area, 3 = volume),
/// which allows dimensional analysis before any value is computed.
/// </summary>
public readonly record struct FormulaType(FormulaKind Kind, int LengthPower = 0)
{
    public static readonly FormulaType Scalar = new(FormulaKind.Number);
    public static readonly FormulaType Boolean = new(FormulaKind.Boolean);
    public static readonly FormulaType Text = new(FormulaKind.Text);

    /// <summary>Returns the formula type that values of a parameter spec have.</summary>
    public static FormulaType ForSpec(ParameterSpec spec) => spec switch
    {
        ParameterSpec.Text => Text,
        ParameterSpec.YesNo => Boolean,
        ParameterSpec.Length => new(FormulaKind.Number, 1),
        ParameterSpec.Area => new(FormulaKind.Number, 2),
        ParameterSpec.Volume => new(FormulaKind.Number, 3),
        _ => Scalar
    };

    /// <summary>Returns a readable description used in error messages, such as "length" or "area".</summary>
    public string Describe() => Kind switch
    {
        FormulaKind.Boolean => "yes/no",
        FormulaKind.Text => "text",
        _ => LengthPower switch { 0 => "number", 1 => "length", 2 => "area", 3 => "volume", _ => $"length^{LengthPower}" }
    };
}

/// <summary>
/// Checks a parsed formula without evaluating it: references exist, operators
/// receive compatible kinds, and lengths, areas, and volumes combine
/// consistently (length + area is rejected; length × length is an area).
/// </summary>
public static class FormulaTypeChecker
{
    private static readonly string[] Trigonometric = ["sin", "cos", "tan"];
    private static readonly string[] InverseTrigonometric = ["asin", "acos", "atan"];
    private static readonly string[] Dimensionless = ["ln", "log", "exp"];

    /// <summary>Returns the type of a formula or throws <see cref="InvalidDataException"/> with the reason.</summary>
    public static FormulaType Check(FormulaNode node, Func<string, FormulaType?> resolve) => node switch
    {
        NumberNode number => new FormulaType(FormulaKind.Number, number.LengthPower),
        TextNode => FormulaType.Text,
        ReferenceNode reference => resolve(reference.Name)
            ?? throw Fail($"Unknown name '{reference.Name}'"),
        UnaryNode { Operator: "-" } unary => RequireNumber(Check(unary.Operand, resolve), "-"),
        UnaryNode unary => RequireKind(Check(unary.Operand, resolve), FormulaKind.Boolean, unary.Operator),
        BinaryNode binary => CheckBinary(binary, resolve),
        CallNode call => CheckCall(call, resolve),
        _ => throw Fail("Unsupported expression")
    };

    /// <summary>Applies operator typing rules, including dimensional analysis for * / ^.</summary>
    private static FormulaType CheckBinary(BinaryNode binary, Func<string, FormulaType?> resolve)
    {
        var left = Check(binary.Left, resolve);
        var right = Check(binary.Right, resolve);
        // A bare 0 has no unit, so "TopOffset > 0" or "Width - 0" stay valid.
        if (binary.Operator is "+" or "-" or "=" or "<>" or "<" or "<=" or ">" or ">=")
        {
            if (IsUnitlessZero(binary.Left) && right.Kind == FormulaKind.Number) left = right;
            if (IsUnitlessZero(binary.Right) && left.Kind == FormulaKind.Number) right = left;
        }
        switch (binary.Operator)
        {
            case "and" or "or":
                RequireKind(left, FormulaKind.Boolean, binary.Operator);
                RequireKind(right, FormulaKind.Boolean, binary.Operator);
                return FormulaType.Boolean;
            case "=" or "<>" when left.Kind == FormulaKind.Text || right.Kind == FormulaKind.Text:
                if (left != right) throw Fail($"Cannot compare {left.Describe()} with {right.Describe()}");
                return FormulaType.Boolean;
            case "=" or "<>" or "<" or "<=" or ">" or ">=":
                RequireSame(RequireNumber(left, binary.Operator), RequireNumber(right, binary.Operator), binary.Operator);
                return FormulaType.Boolean;
            case "+" or "-":
                return RequireSame(RequireNumber(left, binary.Operator), RequireNumber(right, binary.Operator), binary.Operator);
            case "*":
                return new FormulaType(FormulaKind.Number,
                    RequireNumber(left, "*").LengthPower + RequireNumber(right, "*").LengthPower);
            case "/":
                return new FormulaType(FormulaKind.Number,
                    RequireNumber(left, "/").LengthPower - RequireNumber(right, "/").LengthPower);
            case "^":
                RequireNumber(left, "^");
                if (RequireNumber(right, "^").LengthPower != 0) throw Fail("An exponent must be a plain number");
                if (left.LengthPower == 0) return left;
                if (binary.Right is NumberNode { Value: var exponent, LengthPower: 0 } && exponent == Math.Round(exponent))
                    return new FormulaType(FormulaKind.Number, left.LengthPower * (int)exponent);
                throw Fail("A length can only be raised to a whole-number literal");
            default:
                throw Fail($"Unknown operator '{binary.Operator}'");
        }
    }

    /// <summary>Applies function typing rules.</summary>
    private static FormulaType CheckCall(CallNode call, Func<string, FormulaType?> resolve)
    {
        var args = call.Arguments.Select(argument => Check(argument, resolve)).ToArray();
        var name = call.Function;
        switch (name)
        {
            case "if":
                RequireCount(call, 3);
                RequireKind(args[0], FormulaKind.Boolean, "if");
                if (args[1] != args[2])
                    throw Fail($"Both results of if() must have the same type ({args[1].Describe()} vs {args[2].Describe()})");
                return args[1];
            case "and" or "or":
                if (args.Length < 1) throw Fail($"{name}() needs at least one argument");
                foreach (var arg in args) RequireKind(arg, FormulaKind.Boolean, name);
                return FormulaType.Boolean;
            case "not":
                RequireCount(call, 1);
                return RequireKind(args[0], FormulaKind.Boolean, name);
            case "abs" or "round" or "roundup" or "rounddown":
                if (args.Length is < 1 or > 2) throw Fail($"{name}() takes one or two arguments");
                RequireNumber(args[0], name);
                if (args.Length == 2 && RequireNumber(args[1], name).LengthPower is var step &&
                    step != 0 && step != args[0].LengthPower)
                    throw Fail($"The rounding step of {name}() must be a plain number or match its value");
                if (name == "abs" && args.Length != 1) throw Fail("abs() takes one argument");
                return args[0];
            case "min" or "max":
                if (args.Length < 1) throw Fail($"{name}() needs at least one argument");
                foreach (var arg in args) RequireSame(RequireNumber(args[0], name), RequireNumber(arg, name), name);
                return args[0];
            case "sqrt":
                RequireCount(call, 1);
                if (RequireNumber(args[0], name).LengthPower % 2 != 0)
                    throw Fail("sqrt() needs an area, a plain number, or another even power of length");
                return new FormulaType(FormulaKind.Number, args[0].LengthPower / 2);
            case "pi":
                RequireCount(call, 0);
                return FormulaType.Scalar;
            default:
                if (Trigonometric.Contains(name) || InverseTrigonometric.Contains(name) || Dimensionless.Contains(name))
                {
                    RequireCount(call, 1);
                    if (RequireNumber(args[0], name).LengthPower != 0) throw Fail($"{name}() needs a plain number or angle");
                    return FormulaType.Scalar;
                }
                throw Fail($"Unknown function '{name}'");
        }
    }

    /// <summary>Returns whether a node is the literal 0 without a unit suffix.</summary>
    private static bool IsUnitlessZero(FormulaNode node) => node is NumberNode { Value: 0, LengthPower: 0 };

    /// <summary>Requires an exact argument count.</summary>
    private static void RequireCount(CallNode call, int count)
    {
        if (call.Arguments.Count != count) throw Fail($"{call.Function}() takes {count} argument(s)");
    }

    /// <summary>Requires a numeric operand.</summary>
    private static FormulaType RequireNumber(FormulaType type, string operation) =>
        type.Kind == FormulaKind.Number ? type : throw Fail($"'{operation}' needs a number, not {type.Describe()}");

    /// <summary>Requires an operand of a given kind.</summary>
    private static FormulaType RequireKind(FormulaType type, FormulaKind kind, string operation) =>
        type.Kind == kind ? type : throw Fail($"'{operation}' needs {new FormulaType(kind).Describe()}, not {type.Describe()}");

    /// <summary>Requires two numeric operands with the same power of length.</summary>
    private static FormulaType RequireSame(FormulaType left, FormulaType right, string operation) =>
        left.LengthPower == right.LengthPower ? left
            : throw Fail($"Inconsistent units in '{operation}': {left.Describe()} and {right.Describe()}");

    /// <summary>Builds a formula type error.</summary>
    private static InvalidDataException Fail(string reason) => new($"Formula error: {reason}.");
}
