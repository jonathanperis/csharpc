// A custom attribute argument naming a constructed generic type, typeof(Converter<string>): in a compiled
// assembly the blob holds the CLR name "Converter`1[[System.String, System.Runtime, ...]]" (what csc writes for
// [JsonConverter(typeof(SingleItemOrListConverter<ContentBlock>))]). Also nested generics, arrays and open generics.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

Console.WriteLine(JsonSerializer.Serialize(new Box { Items = ["a"] }, Ctx.Default.Box));
Console.WriteLine(JsonSerializer.Serialize(new Box { Items = ["a", "b"] }, Ctx.Default.Box));
Console.WriteLine(JsonSerializer.Deserialize("{\"Items\":\"x\"}", Ctx.Default.Box)!.Items[0]);

foreach (var a in typeof(Tagged).GetCustomAttributes(false))
    if (a is TypeListAttribute list)
        foreach (var t in list.Types) Console.WriteLine(t);

[AttributeUsage(AttributeTargets.All)]
class TypeListAttribute(params Type[] types) : Attribute
{
    public Type[] Types { get; } = types;
}

[TypeList(typeof(Pair<string, int>), typeof(Pair<List<string>, Dictionary<int, string[]>>), typeof(Pair<int[], int>), typeof(Pair<,>), typeof(Pair<string, int>[]), typeof(Outer.Inner<long>))]
class Tagged;

class Pair<A, B>;

class Outer
{
    public class Inner<T>;
}

class Box
{
    [JsonConverter(typeof(SingleItemOrListConverter<string>))]
    public List<string> Items { get; set; } = [];
}

class SingleItemOrListConverter<T> : JsonConverter<List<T>>
{
    public override List<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var item = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        if (reader.TokenType != JsonTokenType.StartArray)
            return [JsonSerializer.Deserialize(ref reader, item)!];
        var list = new List<T>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(JsonSerializer.Deserialize(ref reader, item)!);
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        var item = (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T));
        if (value.Count == 1)
        {
            JsonSerializer.Serialize(writer, value[0], item);
            return;
        }
        writer.WriteStartArray();
        foreach (var v in value)
            JsonSerializer.Serialize(writer, v, item);
        writer.WriteEndArray();
    }
}

[JsonSerializable(typeof(Box))]
[JsonSerializable(typeof(string))]
partial class Ctx : JsonSerializerContext;
