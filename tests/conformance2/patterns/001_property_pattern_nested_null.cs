static string M(object? o) => o switch { Box { Inner: { Value: > 0 } } => "positive inner", Box { Inner: null } => "empty box", Box => "box", _ => "other" };
Console.WriteLine(M(new Box(new Inner(5))) + "|" + M(new Box(null)) + "|" + M(new Box(new Inner(-1))) + "|" + M(null));
record Box(Inner? Inner);
record Inner(int Value);
