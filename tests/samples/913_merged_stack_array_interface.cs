// `list ?? (IEnumerable<T>)[]`, as Npgsql's data source builders write it: csc merges the list and
// Array.Empty<T>() in one evaluation-stack slot typed as the interface, so through IL the array is used
// as IEnumerable<T> without any conversion instruction, and its interface map must still include it.
using System;
using System.Collections.Generic;

Console.WriteLine(new DataSourceBuilder().Build());
Console.WriteLine(new DataSourceBuilder().Configure(o => o.Count += 2).Configure(o => o.Count *= 5).Build());
Console.WriteLine(Pick(null).GetType().Name + " " + Pick(new List<string> { "x" }).GetType().Name);

static IReadOnlyList<string> Pick(List<string>? list) => list ?? (IReadOnlyList<string>)Array.Empty<string>();

sealed class OptionsBuilder { public int Count; }
sealed class TracingBuilder { public int Count = 1; }

sealed class DataSourceBuilder
{
    List<Action<OptionsBuilder>>? _options;
    List<Action<TracingBuilder>>? _tracing;

    public DataSourceBuilder Configure(Action<OptionsBuilder> action)
    {
        _options ??= new();
        _options.Add(action);
        return this;
    }

    public int Build()
    {
        var options = new OptionsBuilder();
        foreach (var callback in _options ?? (IEnumerable<Action<OptionsBuilder>>)[])
            callback.Invoke(options);
        var tracing = new TracingBuilder();
        foreach (var callback in _tracing ?? (IEnumerable<Action<TracingBuilder>>)[])
            callback.Invoke(tracing);
        return options.Count + tracing.Count;
    }
}
