// A generic static abstract interface method (`static abstract bool Check<TState>(...)`), implemented explicitly by a
// struct nested in a generic class and called through a constrained type parameter with the caller's own type
// argument (the shape of System.Text.RegularExpressions' INullabilityHandler.IsNullableAt<TStateHandler>).
using System;

var matcher = new Matcher<int>();
Console.WriteLine(matcher.Run<Matcher<int>.Plain, StateA>(5, 1));
Console.WriteLine(matcher.Run<Matcher<int>.Plain, StateB>(5, 2));
Console.WriteLine(matcher.Run<Matcher<int>.Loud, StateA>(5, 3));
Console.WriteLine(matcher.Run<Matcher<int>.Loud, StateB>(5, 4));
var other = new Matcher<ulong>();
Console.WriteLine(other.Run<Matcher<ulong>.Plain, StateB>(9UL, 5));

interface IStateHandler
{
    static abstract int Id { get; }
}

struct StateA : IStateHandler { public static int Id => 7; }
struct StateB : IStateHandler { public static int Id => 11; }

class Matcher<TSet> where TSet : struct
{
    public interface IHandler
    {
        static abstract string Describe<TState>(TSet set, int position) where TState : struct, IStateHandler;
    }

    public struct Plain : IHandler
    {
        static string IHandler.Describe<TState>(TSet set, int position) => $"plain {typeof(TSet).Name} state={TState.Id} at {position}";
    }

    public struct Loud : IHandler
    {
        public static string Describe<TState>(TSet set, int position) where TState : struct, IStateHandler => $"LOUD {set} state={TState.Id} at {position}";
    }

    public string Run<THandler, TState>(TSet set, int position)
        where THandler : struct, IHandler
        where TState : struct, IStateHandler
        => THandler.Describe<TState>(set, position);
}
