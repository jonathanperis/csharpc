var p = new Pt { X = 1, Y = 2 };
Console.WriteLine(p.X + p.Y);
var q = new Box { Inner = { Value = 9 } };
Console.WriteLine(q.Inner.Value);
class Pt { public int X { get; set; } public int Y { get; set; } }
class Holder { public int Value; }
class Box { public Holder Inner { get; } = new Holder(); }
