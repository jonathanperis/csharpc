// A tokenizer, recursive-descent parser and tree-walking interpreter for a tiny language ("Lox-lite").
using System.Text;

const string source = """
    let fib = fn(n) { if (n < 2) { return n; } return fib(n - 1) + fib(n - 2); };
    let i = 0;
    while (i < 10) { print("fib(" + str(i) + ") = " + str(fib(i))); i = i + 1; }

    let makeCounter = fn() { let count = 0; return fn() { count = count + 1; return count; }; };
    let c1 = makeCounter();
    let c2 = makeCounter();
    c1(); c1();
    print("c1=" + str(c1()) + " c2=" + str(c2()));

    let list = [3, 1, 4, 1, 5, 9, 2, 6];
    let sum = 0;
    for (x in list) { sum = sum + x; }
    print("sum=" + str(sum) + " len=" + str(len(list)));

    let map = fn(f, xs) { let out = []; for (x in xs) { push(out, f(x)); } return out; };
    print(str(map(fn(x) { return x * x; }, [1, 2, 3, 4])));

    let person = { "name": "Ada", "age": 36 };
    print(person["name"] + " is " + str(person["age"]));
    person["age"] = person["age"] + 1;
    print("next year: " + str(person["age"]));
    print(str(1 / 3 * 3 == 1) + " " + str(7 % 3) + " " + str(-2.5 * 2) + " " + str(!true || false));
    print(undefinedVariable);
    """;

var interpreter = new Interpreter();
try
{
    var tokens = new Lexer(source).Tokenize();
    Console.WriteLine($"{tokens.Count} tokens");
    var program = new Parser(tokens).ParseProgram();
    Console.WriteLine($"{program.Count} statements");
    interpreter.Run(program);
}
catch (LangError e)
{
    Console.WriteLine($"error at line {e.Line}: {e.Message}");
}
Console.WriteLine($"steps executed: {interpreter.Steps}");

foreach (var bad in new[] { "let = 5;", "print(1 +);", "let s = \"unterminated;", "1 + true;" })
{
    try
    {
        var stmts = new Parser(new Lexer(bad).Tokenize()).ParseProgram();
        new Interpreter().Run(stmts);
        Console.WriteLine("ok?!");
    }
    catch (LangError e) { Console.WriteLine($"{bad,-26} -> {e.GetType().Name}: {e.Message}"); }
}

// ---------------------------------------------------------------- lexer
enum TokenKind { Number, String, Identifier, Keyword, Symbol, Eof }

readonly record struct Token(TokenKind Kind, string Text, int Line)
{
    public override string ToString() => $"{Kind}({Text})";
}

class LangError(string message, int line) : Exception(message) { public int Line { get; } = line; }
class SyntaxError(string message, int line) : LangError(message, line);
class RuntimeError(string message, int line) : LangError(message, line);

class Lexer(string text)
{
    static readonly HashSet<string> Keywords = ["let", "fn", "if", "else", "while", "for", "in", "return", "true", "false", "nil"];
    static readonly string[] TwoCharSymbols = ["==", "!=", "<=", ">=", "&&", "||"];
    int _pos, _line = 1;

    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= text.Length) { tokens.Add(new Token(TokenKind.Eof, "", _line)); return tokens; }
            char c = text[_pos];
            if (char.IsDigit(c)) tokens.Add(Number());
            else if (char.IsLetter(c) || c == '_') tokens.Add(Word());
            else if (c == '"') tokens.Add(StringLiteral());
            else tokens.Add(Symbol());
        }
    }

    void SkipWhitespace()
    {
        while (_pos < text.Length && char.IsWhiteSpace(text[_pos]))
        {
            if (text[_pos] == '\n') _line++;
            _pos++;
        }
    }

    Token Number()
    {
        int start = _pos;
        while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
        return new Token(TokenKind.Number, text[start.._pos], _line);
    }

    Token Word()
    {
        int start = _pos;
        while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_')) _pos++;
        var word = text.Substring(start, _pos - start);
        return new Token(Keywords.Contains(word) ? TokenKind.Keyword : TokenKind.Identifier, word, _line);
    }

    Token StringLiteral()
    {
        var sb = new StringBuilder();
        _pos++;
        while (_pos < text.Length && text[_pos] != '"')
        {
            if (text[_pos] == '\\' && _pos + 1 < text.Length)
            {
                _pos++;
                sb.Append(text[_pos] switch { 'n' => '\n', 't' => '\t', var other => other });
            }
            else sb.Append(text[_pos]);
            _pos++;
        }
        if (_pos >= text.Length) throw new SyntaxError("unterminated string", _line);
        _pos++;
        return new Token(TokenKind.String, sb.ToString(), _line);
    }

    Token Symbol()
    {
        foreach (var s in TwoCharSymbols)
            if (_pos + 1 < text.Length && text[_pos] == s[0] && text[_pos + 1] == s[1])
            {
                _pos += 2;
                return new Token(TokenKind.Symbol, s, _line);
            }
        return new Token(TokenKind.Symbol, text[_pos++].ToString(), _line);
    }
}

// ---------------------------------------------------------------- AST
abstract record Expr(int Line);
record NumberExpr(double Value, int Line) : Expr(Line);
record StringExpr(string Value, int Line) : Expr(Line);
record BoolExpr(bool Value, int Line) : Expr(Line);
record NilExpr(int Line) : Expr(Line);
record VarExpr(string Name, int Line) : Expr(Line);
record AssignExpr(Expr Target, Expr Value, int Line) : Expr(Line);
record UnaryExpr(string Op, Expr Operand, int Line) : Expr(Line);
record BinaryExpr(string Op, Expr Left, Expr Right, int Line) : Expr(Line);
record CallExpr(Expr Callee, List<Expr> Args, int Line) : Expr(Line);
record IndexExpr(Expr Target, Expr Index, int Line) : Expr(Line);
record ListExpr(List<Expr> Items, int Line) : Expr(Line);
record MapExpr(List<(Expr Key, Expr Value)> Entries, int Line) : Expr(Line);
record FnExpr(List<string> Params, List<Stmt> Body, int Line) : Expr(Line);

abstract record Stmt(int Line);
record LetStmt(string Name, Expr Value, int Line) : Stmt(Line);
record ExprStmt(Expr Expr, int Line) : Stmt(Line);
record IfStmt(Expr Cond, List<Stmt> Then, List<Stmt>? Else, int Line) : Stmt(Line);
record WhileStmt(Expr Cond, List<Stmt> Body, int Line) : Stmt(Line);
record ForStmt(string Var, Expr Iterable, List<Stmt> Body, int Line) : Stmt(Line);
record ReturnStmt(Expr? Value, int Line) : Stmt(Line);

// ---------------------------------------------------------------- parser
class Parser(List<Token> tokens)
{
    int _pos;
    Token Peek => tokens[_pos];
    Token Advance() => tokens[_pos++];
    bool Check(string text) => Peek.Kind is TokenKind.Symbol or TokenKind.Keyword && Peek.Text == text;
    bool Match(string text) { if (!Check(text)) return false; _pos++; return true; }

    Token Expect(string text)
    {
        if (!Check(text)) throw new SyntaxError($"expected '{text}' but found '{Peek.Text}'", Peek.Line);
        return Advance();
    }

    public List<Stmt> ParseProgram()
    {
        var stmts = new List<Stmt>();
        while (Peek.Kind != TokenKind.Eof) stmts.Add(Statement());
        return stmts;
    }

    List<Stmt> Block()
    {
        Expect("{");
        var stmts = new List<Stmt>();
        while (!Check("}")) stmts.Add(Statement());
        Expect("}");
        return stmts;
    }

    Stmt Statement()
    {
        int line = Peek.Line;
        if (Match("let"))
        {
            var name = Advance();
            if (name.Kind != TokenKind.Identifier) throw new SyntaxError($"expected a name after 'let', found '{name.Text}'", name.Line);
            Expect("=");
            var value = Expression();
            Expect(";");
            return new LetStmt(name.Text, value, line);
        }
        if (Match("if"))
        {
            Expect("("); var cond = Expression(); Expect(")");
            var then = Block();
            List<Stmt>? otherwise = Match("else") ? (Check("if") ? [Statement()] : Block()) : null;
            return new IfStmt(cond, then, otherwise, line);
        }
        if (Match("while")) { Expect("("); var cond = Expression(); Expect(")"); return new WhileStmt(cond, Block(), line); }
        if (Match("for"))
        {
            Expect("("); var name = Advance().Text; Expect("in"); var iterable = Expression(); Expect(")");
            return new ForStmt(name, iterable, Block(), line);
        }
        if (Match("return"))
        {
            Expr? value = Check(";") ? null : Expression();
            Expect(";");
            return new ReturnStmt(value, line);
        }
        var expr = Expression();
        Expect(";");
        return new ExprStmt(expr, line);
    }

    Expr Expression() => Assignment();

    Expr Assignment()
    {
        var left = Or();
        if (Check("="))
        {
            int line = Advance().Line;
            var value = Assignment();
            return left is VarExpr or IndexExpr ? new AssignExpr(left, value, line) : throw new SyntaxError("invalid assignment target", line);
        }
        return left;
    }

    Expr Or() => LeftAssoc(And, "||");
    Expr And() => LeftAssoc(Equality, "&&");
    Expr Equality() => LeftAssoc(Comparison, "==", "!=");
    Expr Comparison() => LeftAssoc(Term, "<", "<=", ">", ">=");
    Expr Term() => LeftAssoc(Factor, "+", "-");
    Expr Factor() => LeftAssoc(Unary, "*", "/", "%");

    Expr LeftAssoc(Func<Expr> next, params string[] ops)
    {
        var left = next();
        while (ops.Any(Check))
        {
            var op = Advance();
            left = new BinaryExpr(op.Text, left, next(), op.Line);
        }
        return left;
    }

    Expr Unary()
    {
        if (Check("-") || Check("!")) { var op = Advance(); return new UnaryExpr(op.Text, Unary(), op.Line); }
        return Postfix();
    }

    Expr Postfix()
    {
        var expr = Primary();
        while (true)
        {
            if (Match("("))
            {
                var args = new List<Expr>();
                if (!Check(")")) do args.Add(Expression()); while (Match(","));
                Expect(")");
                expr = new CallExpr(expr, args, expr.Line);
            }
            else if (Match("[")) { var index = Expression(); Expect("]"); expr = new IndexExpr(expr, index, expr.Line); }
            else return expr;
        }
    }

    Expr Primary()
    {
        var t = Advance();
        switch (t.Kind)
        {
            case TokenKind.Number: return new NumberExpr(double.Parse(t.Text, System.Globalization.CultureInfo.InvariantCulture), t.Line);
            case TokenKind.String: return new StringExpr(t.Text, t.Line);
            case TokenKind.Identifier: return new VarExpr(t.Text, t.Line);
            case TokenKind.Keyword when t.Text is "true" or "false": return new BoolExpr(t.Text == "true", t.Line);
            case TokenKind.Keyword when t.Text == "nil": return new NilExpr(t.Line);
            case TokenKind.Keyword when t.Text == "fn":
            {
                Expect("(");
                var ps = new List<string>();
                if (!Check(")")) do ps.Add(Advance().Text); while (Match(","));
                Expect(")");
                return new FnExpr(ps, Block(), t.Line);
            }
        }
        if (t.Text == "(") { var e = Expression(); Expect(")"); return e; }
        if (t.Text == "[")
        {
            var items = new List<Expr>();
            if (!Check("]")) do items.Add(Expression()); while (Match(","));
            Expect("]");
            return new ListExpr(items, t.Line);
        }
        if (t.Text == "{")
        {
            var entries = new List<(Expr, Expr)>();
            if (!Check("}")) do { var k = Expression(); Expect(":"); entries.Add((k, Expression())); } while (Match(","));
            Expect("}");
            return new MapExpr(entries, t.Line);
        }
        throw new SyntaxError($"unexpected '{t.Text}'", t.Line);
    }
}

// ---------------------------------------------------------------- runtime values
abstract class Callable { public abstract object? Call(Interpreter interp, List<object?> args, int line); }

sealed class Closure(FnExpr fn, Environment env) : Callable
{
    public override object? Call(Interpreter interp, List<object?> args, int line)
    {
        if (args.Count != fn.Params.Count) throw new RuntimeError($"expected {fn.Params.Count} arguments but got {args.Count}", line);
        var scope = new Environment(env);
        for (int i = 0; i < args.Count; i++) scope.Define(fn.Params[i], args[i]);
        try { interp.ExecuteBlock(fn.Body, scope); }
        catch (ReturnSignal r) { return r.Value; }
        return null;
    }
    public override string ToString() => $"<fn/{fn.Params.Count}>";
}

sealed class Builtin(string name, Func<List<object?>, int, object?> body) : Callable
{
    public override object? Call(Interpreter interp, List<object?> args, int line) => body(args, line);
    public override string ToString() => $"<builtin {name}>";
}

sealed class ReturnSignal(object? value) : Exception { public object? Value { get; } = value; }

class Environment(Environment? parent)
{
    readonly Dictionary<string, object?> _values = new();
    public void Define(string name, object? value) => _values[name] = value;

    public object? Get(string name, int line) =>
        _values.TryGetValue(name, out var v) ? v
        : parent is not null ? parent.Get(name, line)
        : throw new RuntimeError($"undefined variable '{name}'", line);

    public void Assign(string name, object? value, int line)
    {
        if (_values.ContainsKey(name)) _values[name] = value;
        else if (parent is not null) parent.Assign(name, value, line);
        else throw new RuntimeError($"undefined variable '{name}'", line);
    }
}

// ---------------------------------------------------------------- interpreter
class Interpreter
{
    readonly Environment _globals = new(null);
    public int Steps { get; private set; }

    public Interpreter()
    {
        _globals.Define("print", new Builtin("print", (a, _) => { Console.WriteLine(Show(a[0])); return null; }));
        _globals.Define("str", new Builtin("str", (a, _) => Show(a[0])));
        _globals.Define("len", new Builtin("len", (a, line) => a[0] switch
        {
            List<object?> l => (double)l.Count,
            string s => (double)s.Length,
            Dictionary<string, object?> d => (double)d.Count,
            _ => throw new RuntimeError("len() of unsupported value", line),
        }));
        _globals.Define("push", new Builtin("push", (a, line) =>
        {
            if (a[0] is not List<object?> list) throw new RuntimeError("push() expects a list", line);
            list.Add(a[1]);
            return null;
        }));
    }

    public void Run(List<Stmt> program) => ExecuteBlock(program, _globals);

    public void ExecuteBlock(List<Stmt> stmts, Environment env)
    {
        foreach (var s in stmts) Execute(s, env);
    }

    void Execute(Stmt stmt, Environment env)
    {
        Steps++;
        switch (stmt)
        {
            case LetStmt(var name, var value, _): env.Define(name, Eval(value, env)); break;
            case ExprStmt(var e, _): Eval(e, env); break;
            case IfStmt(var cond, var then, var otherwise, _):
                if (Truthy(Eval(cond, env))) ExecuteBlock(then, new Environment(env));
                else if (otherwise is not null) ExecuteBlock(otherwise, new Environment(env));
                break;
            case WhileStmt(var cond, var body, _):
                while (Truthy(Eval(cond, env))) ExecuteBlock(body, new Environment(env));
                break;
            case ForStmt(var name, var iterable, var body, var line):
                if (Eval(iterable, env) is not List<object?> items) throw new RuntimeError("for-in expects a list", line);
                foreach (var item in items)
                {
                    var scope = new Environment(env);
                    scope.Define(name, item);
                    ExecuteBlock(body, scope);
                }
                break;
            case ReturnStmt(var value, _): throw new ReturnSignal(value is null ? null : Eval(value, env));
        }
    }

    object? Eval(Expr expr, Environment env) => expr switch
    {
        NumberExpr n => n.Value,
        StringExpr s => s.Value,
        BoolExpr b => b.Value,
        NilExpr => null,
        VarExpr v => env.Get(v.Name, v.Line),
        AssignExpr { Target: VarExpr target } a => Assign(env, target, Eval(a.Value, env)),
        AssignExpr { Target: IndexExpr target } a => SetIndex(Eval(target.Target, env), Eval(target.Index, env), Eval(a.Value, env), a.Line),
        UnaryExpr { Op: "-" } u => -AsNumber(Eval(u.Operand, env), u.Line),
        UnaryExpr { Op: "!" } u => !Truthy(Eval(u.Operand, env)),
        BinaryExpr { Op: "&&" } b => Truthy(Eval(b.Left, env)) && Truthy(Eval(b.Right, env)),
        BinaryExpr { Op: "||" } b => Truthy(Eval(b.Left, env)) || Truthy(Eval(b.Right, env)),
        BinaryExpr b => Binary(b.Op, Eval(b.Left, env), Eval(b.Right, env), b.Line),
        CallExpr c => Call(c, env),
        IndexExpr i => GetIndex(Eval(i.Target, env), Eval(i.Index, env), i.Line),
        ListExpr l => l.Items.Select(item => Eval(item, env)).ToList(),
        MapExpr m => m.Entries.ToDictionary(e => Show(Eval(e.Key, env)), e => Eval(e.Value, env)),
        FnExpr f => new Closure(f, env),
        _ => throw new RuntimeError($"cannot evaluate {expr.GetType().Name}", expr.Line),
    };

    static object? Assign(Environment env, VarExpr target, object? value)
    {
        env.Assign(target.Name, value, target.Line);
        return value;
    }

    object? Call(CallExpr c, Environment env)
    {
        var callee = Eval(c.Callee, env);
        var args = c.Args.Select(a => Eval(a, env)).ToList();
        return callee is Callable f ? f.Call(this, args, c.Line) : throw new RuntimeError($"{Show(callee)} is not callable", c.Line);
    }

    static object? GetIndex(object? target, object? index, int line) => (target, index) switch
    {
        (List<object?> list, double d) when d >= 0 && d < list.Count => list[(int)d],
        (List<object?>, double d) => throw new RuntimeError($"index {d} out of range", line),
        (Dictionary<string, object?> map, string key) => map.TryGetValue(key, out var v) ? v : null,
        (string s, double d) => s[(int)d].ToString(),
        _ => throw new RuntimeError("bad index operation", line),
    };

    static object? SetIndex(object? target, object? index, object? value, int line)
    {
        switch (target, index)
        {
            case (List<object?> list, double d): list[(int)d] = value; break;
            case (Dictionary<string, object?> map, string key): map[key] = value; break;
            default: throw new RuntimeError("bad index assignment", line);
        }
        return value;
    }

    static object? Binary(string op, object? l, object? r, int line) => op switch
    {
        "+" when l is string || r is string => Show(l) + Show(r),
        "+" => AsNumber(l, line) + AsNumber(r, line),
        "-" => AsNumber(l, line) - AsNumber(r, line),
        "*" => AsNumber(l, line) * AsNumber(r, line),
        "/" => AsNumber(r, line) == 0 ? throw new RuntimeError("division by zero", line) : AsNumber(l, line) / AsNumber(r, line),
        "%" => AsNumber(l, line) % AsNumber(r, line),
        "<" => AsNumber(l, line) < AsNumber(r, line),
        "<=" => AsNumber(l, line) <= AsNumber(r, line),
        ">" => AsNumber(l, line) > AsNumber(r, line),
        ">=" => AsNumber(l, line) >= AsNumber(r, line),
        "==" => Equals(l, r),
        "!=" => !Equals(l, r),
        _ => throw new RuntimeError($"unknown operator {op}", line),
    };

    static double AsNumber(object? v, int line) => v is double d ? d : throw new RuntimeError($"expected a number, got {Show(v)}", line);
    static bool Truthy(object? v) => v switch { null => false, bool b => b, double d => d != 0, _ => true };

    public static string Show(object? v) => v switch
    {
        null => "nil",
        bool b => b ? "true" : "false",
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        string s => s,
        List<object?> list => "[" + string.Join(", ", list.Select(Show)) + "]",
        Dictionary<string, object?> map => "{" + string.Join(", ", map.Select(kv => $"{kv.Key}: {Show(kv.Value)}")) + "}",
        _ => v.ToString() ?? "?",
    };
}
