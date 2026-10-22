// A tiny ray tracer rendering a scene to ASCII art: structs, operator overloading, interfaces.
var scene = new Scene
{
    Camera = new Camera(new Vec3(0, 1.2, -4.5), new Vec3(0, 0.6, 0), fieldOfView: 55),
    Light = new Vec3(-4, 6, -5),
};
scene.Add(new Sphere(new Vec3(0, 1, 0), 1, new Material(0.9, 0.3)));
scene.Add(new Sphere(new Vec3(-1.8, 0.6, 1.2), 0.6, new Material(0.6, 0.0)));
scene.Add(new Sphere(new Vec3(1.7, 0.45, -0.6), 0.45, new Material(0.8, 0.6)));
scene.Add(new Plane(new Vec3(0, 0, 0), new Vec3(0, 1, 0), new Material(0.5, 0.1), checkered: true));

const int width = 72, height = 30;
const string ramp = " .:-=+*#%@";
var image = scene.Render(width, height);
var histogram = new int[ramp.Length];
for (int y = 0; y < height; y++)
{
    var line = new char[width];
    for (int x = 0; x < width; x++)
    {
        var b = Math.Clamp(image[y, x], 0, 0.9999);
        var index = (int)(b * ramp.Length);
        histogram[index]++;
        line[x] = ramp[index];
    }
    Console.WriteLine(new string(line).TrimEnd());
}
Console.WriteLine($"rays: {scene.RaysCast}, hits: {scene.Hits}, mean brightness: {Enumerable.Range(0, height).SelectMany(y => Enumerable.Range(0, width).Select(x => image[y, x])).Average():F4}");
Console.WriteLine("histogram: " + string.Join(" ", histogram.Select((n, i) => $"'{ramp[i]}'={n}")));

readonly record struct Vec3(double X, double Y, double Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normalized => this * (1 / Length);
    public Vec3 Reflect(Vec3 normal) => this - normal * (2 * Dot(normal));
}

readonly record struct Ray(Vec3 Origin, Vec3 Direction)
{
    public Vec3 At(double t) => Origin + Direction * t;
}

readonly record struct Material(double Diffuse, double Reflectivity);

readonly record struct Hit(double Distance, Vec3 Point, Vec3 Normal, IShape Shape);

interface IShape
{
    Material Material { get; }
    Hit? Intersect(Ray ray);
    double Shade(Vec3 point) => 1.0;
}

sealed class Sphere(Vec3 center, double radius, Material material) : IShape
{
    public Material Material => material;
    public Hit? Intersect(Ray ray)
    {
        var oc = ray.Origin - center;
        var b = oc.Dot(ray.Direction);
        var c = oc.Dot(oc) - radius * radius;
        var discriminant = b * b - c;
        if (discriminant < 0) return null;
        var sqrt = Math.Sqrt(discriminant);
        var t = -b - sqrt;
        if (t < 1e-4) t = -b + sqrt;
        if (t < 1e-4) return null;
        var p = ray.At(t);
        return new Hit(t, p, (p - center).Normalized, this);
    }
}

sealed class Plane(Vec3 point, Vec3 normal, Material material, bool checkered) : IShape
{
    public Material Material => material;
    public Hit? Intersect(Ray ray)
    {
        var denom = normal.Dot(ray.Direction);
        if (Math.Abs(denom) < 1e-9) return null;
        var t = (point - ray.Origin).Dot(normal) / denom;
        return t < 1e-4 ? null : new Hit(t, ray.At(t), normal, this);
    }
    public double Shade(Vec3 p) => !checkered ? 1.0 : ((int)Math.Floor(p.X) + (int)Math.Floor(p.Z)) % 2 == 0 ? 1.0 : 0.45;
}

sealed class Camera(Vec3 position, Vec3 target, double fieldOfView)
{
    private readonly Vec3 _forward = (target - position).Normalized;
    public Ray RayFor(double u, double v, double aspect)
    {
        var right = new Vec3(0, 1, 0).Cross(_forward).Normalized;
        var up = _forward.Cross(right);
        var scale = Math.Tan(fieldOfView * Math.PI / 360);
        var dir = _forward + right * (u * scale * aspect) + up * (v * scale);
        return new Ray(position, dir.Normalized);
    }
}

sealed class Scene
{
    private readonly List<IShape> _shapes = [];
    public required Camera Camera { get; init; }
    public required Vec3 Light { get; init; }
    public long RaysCast { get; private set; }
    public long Hits { get; private set; }

    public void Add(IShape shape) => _shapes.Add(shape);

    public double[,] Render(int width, int height)
    {
        var image = new double[height, width];
        var aspect = width / (height * 2.0);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var u = (2.0 * (x + 0.5) / width - 1) * 1;
                var v = 1 - 2.0 * (y + 0.5) / height;
                image[y, x] = Trace(Camera.RayFor(u, v, aspect * 2), depth: 0);
            }
        return image;
    }

    private Hit? Closest(Ray ray)
    {
        RaysCast++;
        Hit? best = null;
        foreach (var shape in _shapes)
            if (shape.Intersect(ray) is { } h && (best is null || h.Distance < best.Value.Distance))
                best = h;
        if (best is not null) Hits++;
        return best;
    }

    private double Trace(Ray ray, int depth)
    {
        if (Closest(ray) is not { } hit) return 0.05 + 0.1 * Math.Max(0, ray.Direction.Y);
        var toLight = Light - hit.Point;
        var lightDir = toLight.Normalized;
        var shadowed = Closest(new Ray(hit.Point + hit.Normal * 1e-3, lightDir)) is { } blocker && blocker.Distance < toLight.Length;
        var diffuse = shadowed ? 0 : Math.Max(0, hit.Normal.Dot(lightDir)) * hit.Shape.Material.Diffuse;
        var specular = shadowed ? 0 : Math.Pow(Math.Max(0, ray.Direction.Reflect(hit.Normal).Dot(lightDir)), 24) * 0.5;
        var color = (0.08 + diffuse) * hit.Shape.Shade(hit.Point) + specular;
        var r = hit.Shape.Material.Reflectivity;
        if (r > 0 && depth < 3)
            color = color * (1 - r) + Trace(new Ray(hit.Point + hit.Normal * 1e-3, ray.Direction.Reflect(hit.Normal)), depth + 1) * r;
        return color;
    }
}
