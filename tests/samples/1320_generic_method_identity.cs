// Generic code over reference types that looks at itself: a delegate's Method and Target must name the exact instantiation, as .NET does (ADR 0026: csharpc compiles such code once per canonical form under
// --shared-generics, and the canonical form must never be observable). Also a generic class with variant interfaces over T.
#nullable disable
using System;
using System.Collections.Generic;

class Customer { }

interface IProducer<out T> { T Make(); }
interface IConsumer<in T> { string Take(T value); }

class Repo<T> : IProducer<T>, IConsumer<T> where T : class
{
    private readonly T _item;
    public Repo(T item) => _item = item;
    public Func<string> Bound() => Name;
    public string Name() => typeof(T).Name;
    public T Make() => _item;
    public string Take(T value) => value.GetType().Name;
    public bool IsEnumerable(object o) => o is IEnumerable<T>;

}

class Program
{
    static void Main()
    {
        var customers = new Repo<Customer>(new Customer());
        var strings = new Repo<object>("s");
        var bound = customers.Bound();
        Console.WriteLine($"{bound.Method.DeclaringType} {bound.Method.Name} {bound.Target.GetType()} {bound()}");
        var other = strings.Bound();
        Console.WriteLine($"{other.Method.DeclaringType} {other.Target.GetType()} {other()}");

        IProducer<object> producer = customers;          // covariance: IProducer<Customer> as IProducer<object>
        Console.WriteLine(producer.Make().GetType().Name);
        IConsumer<Customer> consumer = strings;          // contravariance: IConsumer<object> as IConsumer<Customer>
        Console.WriteLine(consumer.Take(new Customer()));
        Console.WriteLine($"{producer is IProducer<Customer>} {(object)customers is IConsumer<object>} {(object)strings is IConsumer<string>}");
        Console.WriteLine($"{customers.IsEnumerable(new List<Customer>())} {customers.IsEnumerable(new List<object>())} {strings.IsEnumerable(new List<Customer>())}");
    }
}
