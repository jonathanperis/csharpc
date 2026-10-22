// Copy semantics: arrays of structs, struct fields of classes, readonly fields, rvalues, captures.
var points = new Point[3];
points[1].X = 5;                       // mutates the element in place
points[2] = new Point { X = 7, Y = 8 };
foreach (var p in points) Console.WriteLine(p);
var taken = points[1];
taken.X = 100;                         // copy
Console.WriteLine(points[1].X);
points[0].Move(2, 3);                  // method call mutates the element
Console.WriteLine(points[0]);

var holder = new Holder();
holder.Pos.X = 9;                      // field of a class: mutated in place
holder.Pos.Move(1, 1);
Console.WriteLine(holder.Pos);
holder.Frozen.Move(50, 50);            // readonly field: defensive copy, no effect
Console.WriteLine(holder.Frozen);
Console.WriteLine(holder.Prop.X);      // property returns a copy
var fromProp = holder.Prop;
fromProp.X = 1000;
Console.WriteLine(holder.Prop.X);

Console.WriteLine(MakePoint(4).X);     // member access on an rvalue
Console.WriteLine(MakePoint(2).Sum());

var line = new Line { Start = new Point { X = 1, Y = 1 }, End = new Point { X = 4, Y = 5 } };
var line2 = line;
line2.End.X = 0;
Console.WriteLine($"{line.End.X} {line2.End.X}");
Console.WriteLine(line.Length());

Point q = new Point { X = 1, Y = 2 };
q.X = args.Length > 10 ? 99 : q.X + 10;   // assignment with a branchy right-hand side
Console.WriteLine(q);
Point flip = args.Length == 0 ? q : default;
flip.Y = -1;
Console.WriteLine($"{q.Y} {flip.Y}");

Point[] grid = [new Point { X = 1 }, new Point { X = 2 }];
for (int i = 0; i < grid.Length; i++) grid[i].Y = grid[i].X * 10;
Console.WriteLine($"{grid[0]} {grid[1]}");

static Point MakePoint(int v) => new Point { X = v, Y = v * 2 };

struct Point
{
    public int X;
    public int Y;
    public void Move(int dx, int dy) { X += dx; Y += dy; }
    public readonly int Sum() => X + Y;
    public override readonly string ToString() => $"<{X},{Y}>";
}

struct Line
{
    public Point Start;
    public Point End;
    public readonly double Length()
    {
        int dx = End.X - Start.X, dy = End.Y - Start.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

class Holder
{
    public Point Pos;
    public readonly Point Frozen = new Point { X = 1, Y = 1 };
    public Point Prop { get; set; } = new Point { X = 3, Y = 3 };
}
