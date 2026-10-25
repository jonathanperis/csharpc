#:property PublishAot=false
using System.Text.Json;

// Reflection-based JsonSerializer calls csharpc can't turn into compile-time metadata: anonymous
// types (the JSON source generator rejects them) and file-local types (generated metadata lives in
// another file) are rejected with an explicit diagnostic (323_json_unsupported.diagnostics) instead
// of failing at run time, when the program can reach them (535_unreachable_unsupported: when it can't).
// The first unsupported construct ends a method's lowering, so each call has a method of its own.
Anonymous();
AnonymousArray();
FileLocal();

static void Anonymous() => Console.WriteLine(JsonSerializer.Serialize(new { X = 1, Y = 2 }));
static void AnonymousArray() => Console.WriteLine(JsonSerializer.Serialize(new[] { new { Name = "a" } }));
static void FileLocal() => Console.WriteLine(JsonSerializer.Serialize(new Local(1)));

file record Local(int Value);
