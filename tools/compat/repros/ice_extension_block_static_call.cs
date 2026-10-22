// Internal compiler error (invalid LLVM IR: "Global is external, but doesn't have external or weak
// linkage! ptr @...WordCount..."): calling a C# 14 extension-block method in its static form
// (`E.WordCount(s)`) references a function that is never emitted.
// .NET: 3 3
// Found in dotnet/docs docs/csharp/programming-guide/classes-and-structs/snippets/ExtensionMembers.
using System;

string s = "Hello Extension Methods";
Console.WriteLine(s.WordCount());
Console.WriteLine(E.WordCount(s));

static class E
{
    extension(string str)
    {
        public int WordCount() => str.Split(' ').Length;
    }
}
