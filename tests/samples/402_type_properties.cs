#:property AllowUnsafeBlocks=true
// Type's kind and visibility predicates, BaseType chains, GetElementType / GetArrayRank,
// DeclaringType, Namespace and Name, for user, library, array, pointer and nested types.
using System;
using System.Collections.Generic;

namespace Shop.Model
{
    public abstract class Entity { }
    public sealed class Order : Entity { public struct Line { } }
    internal class Draft : Entity { }
    public static class Catalog { }
    public enum Status : short { Open, Closed }
    public interface IPriced { }
    public record Item(string Name) : IPriced;
    public record struct Price(decimal Amount);
    public delegate void Changed(Order order);
    public ref struct Cursor { }
    public class Outer
    {
        protected internal class Both { }
        private protected class Narrow { }
        public class Visible { }
        protected class Audit { }
        private class Secret { }
        internal class Note { }
        internal static Type[] Nested() => [typeof(Both), typeof(Narrow), typeof(Visible), typeof(Audit), typeof(Secret), typeof(Note)];
    }

    static class Program
    {
        static string Kind(Type t) =>
            (t.IsClass ? "class " : "") + (t.IsValueType ? "valuetype " : "") + (t.IsInterface ? "interface " : "") +
            (t.IsEnum ? "enum " : "") + (t.IsArray ? "array " : "") + (t.IsPointer ? "pointer " : "") + (t.IsPrimitive ? "primitive " : "") +
            (t.IsAbstract ? "abstract " : "") + (t.IsSealed ? "sealed " : "") + (t.IsByRefLike ? "byreflike " : "") + (t.IsByRef ? "byref " : "") +
            (t.HasElementType ? "haselement " : "") + (t.IsTypeDefinition ? "definition " : "");

        static string Visibility(Type t) =>
            (t.IsPublic ? "public " : "") + (t.IsNotPublic ? "notpublic " : "") + (t.IsNested ? "nested " : "") +
            (t.IsNestedPublic ? "nestedpublic " : "") + (t.IsNestedPrivate ? "nestedprivate " : "") + (t.IsNestedFamily ? "nestedfamily " : "") +
            (t.IsNestedAssembly ? "nestedassembly " : "") + (t.IsNestedFamORAssem ? "famorassem " : "") + (t.IsNestedFamANDAssem ? "famandassem " : "") +
            (t.IsVisible ? "visible" : "invisible");

        static string Chain(Type t)
        {
            var parts = new List<string>();
            for (var b = t.BaseType; b != null; b = b.BaseType) parts.Add(b.Name);
            return parts.Count == 0 ? "(none)" : string.Join(" > ", parts);
        }

        static void Show(Type t)
        {
            Console.WriteLine($"{t} | Name={t.Name} Namespace={t.Namespace ?? "(null)"} Declaring={t.DeclaringType?.Name ?? "(null)"}");
            Console.WriteLine($"  {Kind(t)}| {Visibility(t)}");
            Console.WriteLine($"  bases: {Chain(t)}");
        }

        static unsafe void Main()
        {
            Type[] types =
            [
                typeof(Entity), typeof(Order), typeof(Order.Line), typeof(Draft), typeof(Catalog), typeof(Status), typeof(IPriced),
                typeof(Item), typeof(Price), typeof(Changed), typeof(Cursor), typeof(Outer.Visible), typeof(Program),
                typeof(object), typeof(string), typeof(int), typeof(double), typeof(decimal), typeof(nint), typeof(bool), typeof(char),
                typeof(DateTime), typeof(TimeSpan), typeof(ValueType), typeof(Enum), typeof(Array), typeof(Delegate), typeof(Exception),
                typeof(InvalidOperationException), typeof(IDisposable), typeof(IComparable<int>), typeof(Console), typeof(Math),
                typeof(DayOfWeek), typeof(Action), typeof(EventHandler<EventArgs>), typeof(Span<byte>), typeof(int?),
                typeof(List<string>), typeof(List<string>.Enumerator), typeof(Dictionary<int, Order>), typeof(KeyValuePair<int, int>),
                typeof(int[]), typeof(string[][]), typeof(Order[]), typeof(Draft[]), typeof(int[,]), typeof(Status[,,]), typeof(int*), typeof(byte**),
                typeof(Uri), typeof(Version),
            ];
            foreach (var t in types) Show(t);

            Console.WriteLine("-- elements");
            foreach (var t in new[] { typeof(int[]), typeof(string[][]), typeof(Order[,]), typeof(int*), typeof(int), typeof(List<int>) })
                Console.WriteLine($"{t}: element={t.GetElementType()?.ToString() ?? "(null)"} rank={(t.IsArray ? t.GetArrayRank().ToString() : "-")} sz={t.IsSZArray} variable={t.IsVariableBoundArray}");
            try { typeof(string).GetArrayRank(); }
            catch (ArgumentException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

            Console.WriteLine("-- nested visibility");
            foreach (var t in Outer.Nested())
                Console.WriteLine($"{t.Name}: {Visibility(t)} declaring={t.DeclaringType}");

            Console.WriteLine("-- runtime objects");
            object[] values = [new Order(), new Item("pen"), new Price(2.5m), Status.Closed, 3.5, "s", new[] { 1, 2 }, new Exception("x"), (Changed)(o => { }), 1.5m, TimeSpan.FromSeconds(3), new DateTime(2024, 5, 6), (byte)7, 'c', 4L];
            foreach (var v in values)
            {
                var t = v.GetType();
                Console.WriteLine($"{t.Name}: class={t.IsClass} value={t.IsValueType} sealed={t.IsSealed} base={t.BaseType?.Name ?? "(null)"} underlying={t.UnderlyingSystemType == t}");
            }
        }
    }
}
