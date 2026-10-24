// IntrospectionExtensions.GetTypeInfo (Roslyn reaches Type members through it): the TypeInfo is the
// Type itself, so the members it inherits from Type answer the same.
using System.Reflection;

var info = typeof(List<int>).GetTypeInfo();
Console.WriteLine($"{info.Name} {info.IsGenericType} {info.IsClass} {info.IsValueType} {info.Namespace}");
Console.WriteLine(ReferenceEquals(info.AsType(), typeof(List<int>)));
Console.WriteLine(info.AsType() == typeof(List<int>));
Console.WriteLine(DayOfWeek.Monday.GetType().GetTypeInfo().IsEnum);
Console.WriteLine(typeof(Program).GetTypeInfo().Assembly == typeof(Program).Assembly);
try { IntrospectionExtensions.GetTypeInfo(null!); }
catch (ArgumentNullException e) { Console.WriteLine(e.Message); }

partial class Program { }
