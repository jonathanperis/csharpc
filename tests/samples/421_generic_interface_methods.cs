// Generic methods called through an interface (no vtable slot: a closed-world type switch), with
// implementations in plain, derived, generic and struct types and a default interface body.
using System;
using System.Collections.Generic;

ILog[] logs = [new ConsoleLog("a"), new LoudLog("b"), new Wrapper<int>(7), new StructLog(3), new SilentLog(), new Wrapper<string>("s")];
foreach (var log in logs)
{
    log.Write(42);
    log.Write("text");
    log.Write((1, 2.5));
    Console.WriteLine(log.Describe<List<int>>());
    Console.WriteLine(log.Default<char>('x'));
}

IVisitor<string> printer = new Printer();
INode tree = new Add(new Num(1), new Add(new Num(2), new Num(3)));
Console.WriteLine(tree.Accept(printer));
Console.WriteLine(tree.Accept<int>(new Evaluator()));

interface ILog
{
    void Write<T>(T value);
    string Describe<T>();
    string Default<T>(T value) => $"default {typeof(T).Name} {value}";
}

class ConsoleLog(string name) : ILog
{
    public virtual void Write<T>(T value) => Console.WriteLine($"{name}: {value}");
    public string Describe<T>() => $"{name} describes {typeof(T).Name}";
}

class LoudLog(string name) : ConsoleLog(name)
{
    public override void Write<T>(T value) => Console.WriteLine($"LOUD {value?.ToString()?.ToUpperInvariant()}");
}

class Wrapper<TState>(TState state) : ILog
{
    public void Write<T>(T value) => Console.WriteLine($"wrapper[{state}] {value}");
    string ILog.Describe<T>() => $"wrapper of {typeof(TState).Name} describes {typeof(T).Name}";
    public string Default<T>(T value) => $"wrapper default {value}";
}

struct StructLog(int id) : ILog
{
    public void Write<T>(T value) => Console.WriteLine($"struct {id} {value}");
    public string Describe<T>() => $"struct {id} {typeof(T).Name}";
}

sealed class SilentLog : ILog
{
    void ILog.Write<T>(T value) { }
    public string Describe<T>() => "silent";
}

interface IVisitor<TResult>
{
    TResult VisitNum(Num n);
    TResult VisitAdd(Add a);
}

interface INode
{
    TResult Accept<TResult>(IVisitor<TResult> visitor);
}

sealed class Num(int value) : INode
{
    public int Value => value;
    public TResult Accept<TResult>(IVisitor<TResult> visitor) => visitor.VisitNum(this);
}

sealed class Add(INode left, INode right) : INode
{
    public INode Left => left;
    public INode Right => right;
    public TResult Accept<TResult>(IVisitor<TResult> visitor) => visitor.VisitAdd(this);
}

sealed class Printer : IVisitor<string>
{
    public string VisitNum(Num n) => n.Value.ToString();
    public string VisitAdd(Add a) => $"({a.Left.Accept(this)} + {a.Right.Accept(this)})";
}

sealed class Evaluator : IVisitor<int>
{
    public int VisitNum(Num n) => n.Value;
    public int VisitAdd(Add a) => a.Left.Accept(this) + a.Right.Accept(this);
}
