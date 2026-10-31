// Generic variance whose type arguments are interfaces related by inheritance (IDerived : IBase):
// List<IDerived> is an IEnumerable<IBase>, and Roslyn casts its iterators over INamespaceTypeDefinition to
// IEnumerable<INamedTypeDefinition>. csharpc answered False for these (only `object` arguments worked).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

object list = new List<IDerived> { new Impl() };
Console.WriteLine($"list: {list is IEnumerable<IBase>} {list is IReadOnlyList<IBase>} {list is IReadOnlyCollection<IRoot>} {list is IEnumerable<object>} {list is IEnumerable<IOther>}");
object iterator = Iterate();
Console.WriteLine($"iterator: {iterator is IEnumerable<IBase>} {string.Join(",", ((IEnumerable<IBase>)iterator).Select(x => x?.GetType().Name ?? "null"))}");
object generic = new Outer<int, string>().Items();
Console.WriteLine($"generic iterator: {generic is IEnumerable<IRoot>} {((IEnumerable<IBase>)generic).Count()}");
object custom = new MyItems();
Console.WriteLine($"custom: {custom is IEnumerable<IBase>} {((IEnumerable<IBase>)custom).Count()}");
object array = new IDerived[] { new Impl() };
Console.WriteLine($"array: {array is IEnumerable<IBase>} {array is IList<IBase>}");
object nested = new List<List<IDerived>>();
Console.WriteLine($"nested: {nested is IEnumerable<IEnumerable<IBase>>} {nested is IEnumerable<IReadOnlyList<IRoot>>} {nested is IEnumerable<IList<IBase>>}");

// Contravariance: a comparer of the base compares the derived.
object comparer = new BaseComparer();
Console.WriteLine($"contravariant: {comparer is IComparer<IDerived>} {comparer is IComparer<IRoot>}");
var sorted = new List<IDerived> { new Impl { Rank = 2 }, new Impl { Rank = 1 } };
sorted.Sort((IComparer<IDerived>)comparer);
Console.WriteLine($"sorted: {string.Join(",", sorted.Select(x => ((Impl)x).Rank))}");

// Not variant: the other direction, value types, invariant interfaces.
object bases = new List<IBase>();
Console.WriteLine($"negative: {bases is IEnumerable<IDerived>} {(object)new List<int>() is IEnumerable<object>} {list is IList<IBase>} {list is ICollection<IBase>}");
try { _ = (IEnumerable<IDerived>)bases; }
catch (InvalidCastException e) { Console.WriteLine(e.Message); }

static IEnumerable<IDerived> Iterate() { yield return new Impl(); yield return null!; }

interface IRoot { }
interface IBase : IRoot { }
interface IDerived : IBase { }
interface IOther { }
sealed class Impl : IDerived { public int Rank; }

sealed class MyItems : IEnumerable<IDerived>
{
    public IEnumerator<IDerived> GetEnumerator() { yield return new Impl(); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

class Outer<T, U>
{
    public IEnumerable<IDerived> Items() { yield return new Impl(); }
}

sealed class BaseComparer : IComparer<IBase>
{
    public int Compare(IBase? x, IBase? y) => ((Impl)x!).Rank.CompareTo(((Impl)y!).Rank);
}
