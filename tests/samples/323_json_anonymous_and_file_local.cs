#:property JsonSerializerIsReflectionEnabledByDefault=true
using System.Text.Json;

// Reflection-based JsonSerializer over types no generated metadata can name: anonymous types (their constructor is the compiler's,
// the serializer reaches it through reflection) and a file-local type. The serializer is the framework's own code.
Anonymous();
AnonymousArray();
FileLocal();

static void Anonymous() => Console.WriteLine(JsonSerializer.Serialize(new { X = 1, Y = 2 }));
static void AnonymousArray() => Console.WriteLine(JsonSerializer.Serialize(new[] { new { Name = "a" } }));
static void FileLocal() => Console.WriteLine(JsonSerializer.Serialize(new Local(1)));

file record Local(int Value);
