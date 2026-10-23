// Generic types through System.Type: IsGenericType / IsGenericTypeDefinition / IsConstructedGenericType,
// GetGenericTypeDefinition, GetGenericArguments, GenericTypeArguments, generic parameters,
// Nullable.GetUnderlyingType, and FullName / AssemblyQualifiedName of instantiations.
using System;
using System.Collections.Generic;

class Pair<TFirst, TSecond> { public class Inner { } }
class Node<T> where T : class { }
struct Cell<T> where T : struct { }
interface IRepository<TEntity, TKey> { }
class Customer { }
class CustomerRepository : IRepository<Customer, int> { }

static class Program
{
    static void Show(Type t)
    {
        Console.WriteLine($"{t}");
        Console.WriteLine($"  Name={t.Name} FullName={t.FullName ?? "(null)"}");
        Console.WriteLine($"  generic={t.IsGenericType} definition={t.IsGenericTypeDefinition} constructed={t.IsConstructedGenericType} " +
            $"containsParameters={t.ContainsGenericParameters} parameter={t.IsGenericParameter}");
        Console.WriteLine($"  arguments=[{string.Join(", ", (IEnumerable<Type>)t.GetGenericArguments())}] typeArguments={t.GenericTypeArguments.Length}");
        if (t.IsGenericType) Console.WriteLine($"  definition={t.GetGenericTypeDefinition()} same={t.GetGenericTypeDefinition() == t}");
    }

    static string Describe(Type t)
    {
        if (Nullable.GetUnderlyingType(t) is { } underlying) return Describe(underlying) + "?";
        if (t.IsArray) return Describe(t.GetElementType()!) + "[]";
        if (!t.IsGenericType) return t.Name;
        var definition = t.GetGenericTypeDefinition();
        if (definition == typeof(List<>)) return "list of " + Describe(t.GetGenericArguments()[0]);
        if (definition == typeof(Dictionary<,>)) return "map " + Describe(t.GenericTypeArguments[0]) + " -> " + Describe(t.GenericTypeArguments[1]);
        var name = t.Name.Substring(0, t.Name.IndexOf('`'));
        return name + "<" + string.Join(", ", Array.ConvertAll(t.GetGenericArguments(), Describe)) + ">";
    }

    static Type TypeOf<T>() => typeof(T);

    static void Main()
    {
        Show(typeof(List<int>));
        Show(typeof(List<>));
        Show(typeof(Dictionary<string, List<Customer>>));
        Show(typeof(Dictionary<,>));
        Show(typeof(Pair<int, string>.Inner));
        Show(typeof(Pair<,>.Inner));
        Show(typeof(int?));
        Show(typeof(Customer));
        Show(typeof(int[]));
        Show(typeof(List<int>[]));
        Show(typeof(KeyValuePair<string, int>));
        Show(typeof(Func<int, string>));

        Console.WriteLine("-- generic parameters");
        foreach (var definition in new[] { typeof(Dictionary<,>), typeof(Node<>), typeof(Cell<>), typeof(Pair<,>.Inner), typeof(IRepository<,>) })
            foreach (var p in definition.GetGenericArguments())
                Console.WriteLine($"{p.Name} position={p.GenericParameterPosition} declaring={p.DeclaringType} namespace={p.Namespace ?? "(null)"} " +
                    $"class={p.IsClass} valueType={p.IsValueType} base={p.BaseType} fullName={p.FullName ?? "(null)"} aqn={p.AssemblyQualifiedName ?? "(null)"}");
        try { Console.WriteLine(typeof(int).GenericParameterPosition); }
        catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        Console.WriteLine("-- GetGenericTypeDefinition on non-generic types");
        try { typeof(Customer).GetGenericTypeDefinition(); }
        catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
        try { typeof(int[]).GetGenericTypeDefinition(); }
        catch (InvalidOperationException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        Console.WriteLine("-- runtime types");
        object[] values = [new List<string>(), new Dictionary<int, double>(), new Pair<long, Customer>(), new HashSet<int>(), (int?)5, new KeyValuePair<int, int>(1, 2)];
        foreach (var value in values)
        {
            var t = value.GetType();
            Console.WriteLine($"{Describe(t)}: generic={t.IsGenericType} args={t.GenericTypeArguments.Length} isList={t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)}");
        }
        foreach (var t in new[] { typeof(int?), typeof(List<int?>), typeof(Dictionary<string, List<Customer>>), typeof(Cell<double>[]), TypeOf<Pair<int, string>>(), TypeOf<char?>() })
            Console.WriteLine(Describe(t));
        Console.WriteLine(Nullable.GetUnderlyingType(typeof(int)) == null);
        Console.WriteLine(Nullable.GetUnderlyingType(typeof(Nullable<>)) == null);
        try { Nullable.GetUnderlyingType(null!); }
        catch (ArgumentNullException e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }

        Console.WriteLine("-- implemented generic interfaces");
        var repository = typeof(CustomerRepository);
        foreach (var candidate in new[] { typeof(IRepository<Customer, int>), typeof(IRepository<Customer, long>), typeof(IRepository<,>) })
            Console.WriteLine($"{candidate.Name} {candidate.IsAssignableFrom(repository)}");

        Console.WriteLine("-- names");
        Console.WriteLine(typeof(List<int>).AssemblyQualifiedName);
        Console.WriteLine(typeof(Dictionary<string, Customer>).FullName);
        Console.WriteLine(typeof(Pair<int, string>.Inner).FullName);
        Console.WriteLine(typeof(List<int>[]).FullName);
        Console.WriteLine(typeof(List<int>[]).Name);
        Console.WriteLine(typeof(List<>).FullName);
        Console.WriteLine(typeof(List<>).AssemblyQualifiedName);
        Console.WriteLine(new List<Customer>().GetType().FullName);
        Console.WriteLine(typeof(Customer).AssemblyQualifiedName);
    }
}
