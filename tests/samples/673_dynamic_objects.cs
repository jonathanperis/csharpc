// ExpandoObject and DynamicObject through `dynamic`: their meta-object rules (dictionary members, Try* overrides,
// real members first, the binder's errors when a Try* method declines).
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq.Expressions;
using Microsoft.CSharp.RuntimeBinder;

class Bag : DynamicObject
{
    private readonly Dictionary<string, object> _values = new();
    public int Real = 7;
    public string Describe() => "real method";

    public override bool TryGetMember(GetMemberBinder binder, out object result)
    {
        Console.WriteLine($"  TryGetMember {binder.Name} (ignoreCase={binder.IgnoreCase})");
        return _values.TryGetValue(binder.Name, out result);
    }

    public override bool TrySetMember(SetMemberBinder binder, object value)
    {
        Console.WriteLine($"  TrySetMember {binder.Name} = {value}");
        if (binder.Name == "ReadOnly") return false;
        _values[binder.Name] = value;
        return true;
    }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        Console.WriteLine($"  TryInvokeMember {binder.Name} args={args.Length} named={string.Join(",", binder.CallInfo.ArgumentNames)}");
        if (binder.Name == "Shout")
        {
            result = string.Join(" ", args).ToUpperInvariant() + "!";
            return true;
        }
        result = null;
        return false;
    }

    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object result)
    {
        result = "index[" + string.Join(",", indexes) + "]";
        return true;
    }

    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object value)
    {
        Console.WriteLine($"  TrySetIndex [{string.Join(",", indexes)}] = {value}");
        return true;
    }

    public override bool TryBinaryOperation(BinaryOperationBinder binder, object arg, out object result)
    {
        result = binder.Operation == ExpressionType.Add ? "bag+" + arg : null;
        return binder.Operation == ExpressionType.Add;
    }

    public override bool TryUnaryOperation(UnaryOperationBinder binder, out object result)
    {
        result = binder.Operation == ExpressionType.Negate ? "negated bag" : null;
        return result != null;
    }

    public override bool TryConvert(ConvertBinder binder, out object result)
    {
        Console.WriteLine($"  TryConvert to {binder.Type.Name} explicit={binder.Explicit}");
        if (binder.Type == typeof(int)) { result = 42; return true; }
        if (binder.Type == typeof(string)) { result = "bag as string"; return true; }
        result = null;
        return false;
    }

    public override bool TryInvoke(InvokeBinder binder, object[] args, out object result)
    {
        result = "invoked with " + args.Length + " args";
        return true;
    }
}

class Program
{
    static void Main()
    {
        // ExpandoObject: members are dictionary entries.
        dynamic person = new ExpandoObject();
        person.Name = "Ada";
        person.Age = 36;
        person.Age += 1;
        person.Greet = new Func<string, string>(greeting => greeting + ", " + person.Name);
        Console.WriteLine($"{person.Name} {person.Age}");
        Console.WriteLine(person.Greet("Hello"));
        Console.WriteLine(person.ToString());
        var dict = (IDictionary<string, object>)person;
        Console.WriteLine(string.Join(",", dict.Keys));
        dict["Extra"] = 1.5;
        Console.WriteLine(person.Extra * 2);
        try { Console.WriteLine(person.Missing); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        try { person.Missing(); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        dynamic nested = new ExpandoObject();
        nested.Inner = new ExpandoObject();
        nested.Inner.Value = 3;
        Console.WriteLine(nested.Inner.Value);

        // DynamicObject: real members first, then the Try* overrides.
        dynamic bag = new Bag();
        Console.WriteLine(bag.Real);
        Console.WriteLine(bag.Describe());
        bag.Color = "red";
        Console.WriteLine(bag.Color);
        Console.WriteLine(bag.Shout("hi", "there"));
        Console.WriteLine(bag.Shout(word: "named"));
        bag.Callback = new Func<int, int>(x => x * 10);
        Console.WriteLine(bag.Callback(4));
        Console.WriteLine(bag[1, "two"]);
        bag["k"] = 5;
        Console.WriteLine(bag + 1);
        Console.WriteLine(-bag);
        Console.WriteLine(bag(1, 2, 3));
        int asInt = bag;
        Console.WriteLine(asInt);
        Console.WriteLine((string)bag);
        try { Console.WriteLine(bag.Unknown); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        try { bag.ReadOnly = 1; }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        try { bag.Nothing(); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        try { Console.WriteLine(bag * 2); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
        try { double d = bag; Console.WriteLine(d); }
        catch (RuntimeBinderException e) { Console.WriteLine("error: " + e.Message); }
    }
}
