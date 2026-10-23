#:property PublishAot=false
using System.Text.Json;

// Reflection-based JsonSerializer calls csharpc can't turn into compile-time metadata: anonymous
// types (the JSON source generator rejects them) and file-local types (generated metadata lives in
// another file) are rejected with an explicit diagnostic (323_json_unsupported.diagnostics) instead
// of failing at run time.
var point = new { X = 1, Y = 2 };
Console.WriteLine(JsonSerializer.Serialize(point));
Console.WriteLine(JsonSerializer.Serialize(new[] { new { Name = "a" } }));
Console.WriteLine(JsonSerializer.Serialize(new Local(1)));

file record Local(int Value);
