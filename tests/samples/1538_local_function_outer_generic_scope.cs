// #257: lambdas and generic local functions inside generic code calling local functions declared outside that generic
// scope; each callee varies only with the type parameters around its own declaration.
using System;
using System.Collections.Generic;

static string Try(Func<int> f) => f().ToString();
static string Twice(string s) => s + s;
static string Describe<U>(U value) => typeof(U).Name + ":" + value;

void Report<T>(T[] array) => Console.WriteLine(Try(() => array.Length));

void Nested<T>(T value)
{
    Func<string, string> twice = Twice;                    // method group of an outer local function
    string Inner<V>(V v) => Describe(v) + "|" + Describe(value);  // generic local function inside generic code
    Console.WriteLine(twice(Inner(42)) + " " + Try(() => Inner("x").Length));
}

int counter = 0;
void Counting<T>(List<T> items)
{
    void Bump() => counter += items.Count;                  // captures T-typed state: varies with T
    int Outer() => counter * 10;                            // captures only Main's state
    Bump();
    Console.WriteLine($"{typeof(T).Name} {counter} {Try(Outer)}");
}

Report(new[] { 'a' });
Report(new[] { 1, 2 });
Report(new[] { "x", "y", "z" });
Nested(1.5);
Nested("s");
Counting(new List<int> { 1, 2 });
Counting(new List<string> { "a" });
