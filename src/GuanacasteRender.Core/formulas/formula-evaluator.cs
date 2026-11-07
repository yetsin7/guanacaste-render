namespace Guanacaste3D.Core;

/// <summary>A runtime formula value: a number (metric base units), a boolean, or text.</summary>
public readonly record struct FormulaValue(FormulaKind Kind, double Number = 0, string Text = "")
{
    public static FormulaValue Of(double number) => new(FormulaKind.Number, number);
    public static FormulaValue Of(bool value) => new(FormulaKind.Boolean, value ? 1 : 0);
    public static FormulaValue Of(string text) => new(FormulaKind.Text, 0, text);

    /// <summary>Returns the boolean meaning of a boolean value.</summary>
    public bool IsTrue => Number != 0;
}

/// <summary>
/// Evaluates type-checked formulas. Angles are in degrees, lengths in meters.
/// A missing referenced value or an undefined operation (division by zero,
/// square root of a negative number) produces no result instead of a wrong one.
/// </summary>
public static class FormulaEvaluator
{
    /// <summary>Evaluates a formula, returning null when any input is missing or the result is undefined.</summary>
    public static FormulaValue? Evaluate(FormulaNode node, Func<string, FormulaValue?> resolve)
    {
        try
        {
            var value = Eval(node, resolve);
            return value.Kind == FormulaKind.Number && !double.IsFinite(value.Number) ? null : value;
        }
        catch (MissingValueException)
        {
            return null;
        }
    }

    /// <summary>Signals that a referenced value is absent, so the whole formula has no result.</summary>
    private sealed class MissingValueException : Exception;

    /// <summary>Recursively evaluates a node.</summary>
    private static FormulaValue Eval(FormulaNode node, Func<string, FormulaValue?> resolve) => node switch
    {
        NumberNode number => FormulaValue.Of(number.Value),
        TextNode text => FormulaValue.Of(text.Value),
        ReferenceNode reference => resolve(reference.Name) ?? throw new MissingValueException(),
        UnaryNode { Operator: "-" } unary => FormulaValue.Of(-Eval(unary.Operand, resolve).Number),
        UnaryNode unary => FormulaValue.Of(!Eval(unary.Operand, resolve).IsTrue),
        BinaryNode binary => EvalBinary(binary, resolve),
        CallNode call => EvalCall(call, resolve),
        _ => throw new MissingValueException()
    };

    /// <summary>Evaluates operators; and/or short-circuit like the if() function.</summary>
    private static FormulaValue EvalBinary(BinaryNode binary, Func<string, FormulaValue?> resolve)
    {
        if (binary.Operator == "and")
            return FormulaValue.Of(Eval(binary.Left, resolve).IsTrue && Eval(binary.Right, resolve).IsTrue);
        if (binary.Operator == "or")
            return FormulaValue.Of(Eval(binary.Left, resolve).IsTrue || Eval(binary.Right, resolve).IsTrue);
        var left = Eval(binary.Left, resolve);
        var right = Eval(binary.Right, resolve);
        if (left.Kind == FormulaKind.Text)
        {
            var equal = string.Equals(left.Text, right.Text, StringComparison.CurrentCultureIgnoreCase);
            return FormulaValue.Of(binary.Operator == "=" ? equal : !equal);
        }
        double a = left.Number, b = right.Number;
        return binary.Operator switch
        {
            "+" => FormulaValue.Of(a + b),
            "-" => FormulaValue.Of(a - b),
            "*" => FormulaValue.Of(a * b),
            "/" => b == 0 ? throw new MissingValueException() : FormulaValue.Of(a / b),
            "^" => FormulaValue.Of(Math.Pow(a, b)),
            "=" => FormulaValue.Of(NearlyEqual(a, b)),
            "<>" => FormulaValue.Of(!NearlyEqual(a, b)),
            "<" => FormulaValue.Of(a < b && !NearlyEqual(a, b)),
            "<=" => FormulaValue.Of(a < b || NearlyEqual(a, b)),
            ">" => FormulaValue.Of(a > b && !NearlyEqual(a, b)),
            ">=" => FormulaValue.Of(a > b || NearlyEqual(a, b)),
            _ => throw new MissingValueException()
        };
    }

    /// <summary>Evaluates built-in functions; if() evaluates only the chosen branch.</summary>
    private static FormulaValue EvalCall(CallNode call, Func<string, FormulaValue?> resolve)
    {
        var args = call.Arguments;
        double Num(int index) => Eval(args[index], resolve).Number;
        switch (call.Function)
        {
            case "if": return Eval(args[0], resolve).IsTrue ? Eval(args[1], resolve) : Eval(args[2], resolve);
            case "and": return FormulaValue.Of(args.All(argument => Eval(argument, resolve).IsTrue));
            case "or": return FormulaValue.Of(args.Any(argument => Eval(argument, resolve).IsTrue));
            case "not": return FormulaValue.Of(!Eval(args[0], resolve).IsTrue);
            case "abs": return FormulaValue.Of(Math.Abs(Num(0)));
            case "round" or "roundup" or "rounddown":
                var step = args.Count == 2 ? Num(1) : 1;
                if (step <= 0) throw new MissingValueException();
                var scaled = Num(0) / step;
                var rounded = call.Function switch
                {
                    "roundup" => Math.Ceiling(scaled - 1e-9),
                    "rounddown" => Math.Floor(scaled + 1e-9),
                    _ => Math.Round(scaled, MidpointRounding.AwayFromZero)
                };
                return FormulaValue.Of(rounded * step);
            case "min": return FormulaValue.Of(args.Min(argument => Eval(argument, resolve).Number));
            case "max": return FormulaValue.Of(args.Max(argument => Eval(argument, resolve).Number));
            case "sqrt": return Num(0) < 0 ? throw new MissingValueException() : FormulaValue.Of(Math.Sqrt(Num(0)));
            case "pi": return FormulaValue.Of(Math.PI);
            case "sin": return FormulaValue.Of(Math.Sin(Num(0) * Math.PI / 180));
            case "cos": return FormulaValue.Of(Math.Cos(Num(0) * Math.PI / 180));
            case "tan": return FormulaValue.Of(Math.Tan(Num(0) * Math.PI / 180));
            case "asin": return FormulaValue.Of(Math.Asin(Num(0)) * 180 / Math.PI);
            case "acos": return FormulaValue.Of(Math.Acos(Num(0)) * 180 / Math.PI);
            case "atan": return FormulaValue.Of(Math.Atan(Num(0)) * 180 / Math.PI);
            case "ln": return Num(0) <= 0 ? throw new MissingValueException() : FormulaValue.Of(Math.Log(Num(0)));
            case "log": return Num(0) <= 0 ? throw new MissingValueException() : FormulaValue.Of(Math.Log10(Num(0)));
            case "exp": return FormulaValue.Of(Math.Exp(Num(0)));
            default: throw new MissingValueException();
        }
    }

    /// <summary>Compares lengths with a tolerance of 1e-9 so 0.1 + 0.2 = 0.3 holds in formulas.</summary>
    private static bool NearlyEqual(double a, double b) =>
        Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
}
