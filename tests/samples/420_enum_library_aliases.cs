// Enum members that share a value (aliases): .NET picks the name by the order of the implementation
// assembly's fields (reference assemblies list them alphabetically) and sorts with an introsort that is
// not stable beyond 16 members.
using System;
using System.Reflection;

Console.WriteLine(TypeAttributes.NotPublic);
Console.WriteLine(TypeAttributes.Interface);
Console.WriteLine(TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
Console.WriteLine((TypeAttributes)0x20);
Console.WriteLine((TypeAttributes)7);
Console.WriteLine((TypeAttributes)0x18);
Console.WriteLine(MethodAttributes.PrivateScope);
Console.WriteLine(MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual);
Console.WriteLine(MethodAttributes.ReuseSlot);
Console.WriteLine(FieldAttributes.PrivateScope | FieldAttributes.Static);
Console.WriteLine(MethodImplAttributes.IL);
Console.WriteLine(MethodImplAttributes.Managed | MethodImplAttributes.NoInlining);
Console.WriteLine(Enum.GetName(TypeAttributes.Class));
Console.WriteLine(Enum.GetName((TypeAttributes)0x20));
Console.WriteLine(Enum.GetName(MethodAttributes.ReuseSlot));
Console.WriteLine(string.Join(",", Enum.GetNames<TypeAttributes>()));
Console.WriteLine(string.Join(",", Enum.GetNames<MethodAttributes>()));
Console.WriteLine(string.Join(",", Enum.GetNames<System.IO.FileShare>()));
Console.WriteLine($"{TypeAttributes.AnsiClass:F} {TypeAttributes.Abstract | TypeAttributes.Interface:F} {(MethodAttributes)0x10:G}");
Console.WriteLine(Alias.B);
Console.WriteLine(Alias.C);
Console.WriteLine(Many.Z1);

enum Alias { A = 1, B = 1, C = 2, D = 2 }

enum Many
{
    Z0 = 0, Z1 = 0, A = 1, B = 2, C = 3, D = 4, E = 5, F = 6, G = 7, H = 8, I = 9, J = 10, K = 11,
    L = 12, M = 13, N = 14, O = 15, P = 16, Q = 17, Z2 = 0,
}
