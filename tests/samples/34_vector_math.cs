// A small vector-math workload over structs (SROA should keep these in registers).
var bodies = new Body[5];
for (int i = 0; i < bodies.Length; i++)
    bodies[i] = new Body(new Vec3(i, i * 0.5, -i), new Vec3(0.1 * i, 0, 0.05), 1.0 + i);

for (int step = 0; step < 1000; step++)
{
    for (int i = 0; i < bodies.Length; i++)
    {
        var accel = Vec3.Zero;
        for (int j = 0; j < bodies.Length; j++)
        {
            if (i == j) continue;
            var d = bodies[j].Position - bodies[i].Position;
            double dist2 = d.Dot(d) + 0.01;
            accel += d * (bodies[j].Mass / (dist2 * Math.Sqrt(dist2)));
        }
        bodies[i].Velocity += accel * 0.001;
    }
    for (int i = 0; i < bodies.Length; i++)
        bodies[i].Position += bodies[i].Velocity * 0.001;
}

var center = Vec3.Zero;
double totalMass = 0;
foreach (var body in bodies)
{
    center += body.Position * body.Mass;
    totalMass += body.Mass;
}
center = center * (1 / totalMass);
Console.WriteLine($"center of mass: {Math.Round(center.X * 1e6)} {Math.Round(center.Y * 1e6)} {Math.Round(center.Z * 1e6)} (x1e-6)");
Console.WriteLine($"cross: {new Vec3(1, 0, 0).Cross(new Vec3(0, 1, 0))}");

readonly struct Vec3(double x, double y, double z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Z { get; } = z;
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public override string ToString() => $"({X}, {Y}, {Z})";
}

struct Body(Vec3 position, Vec3 velocity, double mass)
{
    public Vec3 Position = position;
    public Vec3 Velocity = velocity;
    public double Mass = mass;
}
