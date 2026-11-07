// System.Numerics.Vector2, Vector3 and Vector4: constructors, constants, the indexer, operators, the static helpers,
// Length, CopyTo, Equals, ToString with a culture and a format; argument errors as .NET (values chosen so that float
// rounding agrees with .NET's SIMD code: sums of products of small integers and exact powers of two).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

static class Program
{
    static string Str<T>(this T value) where T : IFormattable => value.ToString("G", CultureInfo.InvariantCulture);

    static void Try(Action action)
    {
        try { action(); }
        catch (Exception e) { Console.WriteLine($"{e.GetType().Name}: {e.Message}"); }
    }

    static void Main()
    {
        var comma = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        comma.NumberFormat.NumberGroupSeparator = ";";
        var inv = CultureInfo.InvariantCulture;

        // ---- Vector2
        var a2 = new Vector2(3, 4);
        var b2 = new Vector2(1, -2);
        Console.WriteLine(a2.Str());
        Console.WriteLine(new Vector2(2.5f).Str());
        Console.WriteLine($"{Vector2.Zero.Str()} {Vector2.One.Str()} {Vector2.UnitX.Str()} {Vector2.UnitY.Str()}");
        Console.WriteLine((a2 + b2).Str());
        Console.WriteLine((a2 - b2).Str());
        Console.WriteLine((a2 * b2).Str());
        Console.WriteLine((a2 * 2).Str());
        Console.WriteLine((0.5f * a2).Str());
        Console.WriteLine((a2 / b2).Str());
        Console.WriteLine((a2 / 2).Str());
        Console.WriteLine((-a2).Str());
        Console.WriteLine(a2 == new Vector2(3, 4));
        Console.WriteLine(a2 != new Vector2(3, 4));
        Console.WriteLine(a2.Length());
        Console.WriteLine(a2.LengthSquared());
        Console.WriteLine(Vector2.Dot(a2, b2));
        Console.WriteLine(Vector2.Distance(a2, b2));
        Console.WriteLine(Vector2.DistanceSquared(a2, b2));
        Console.WriteLine(Vector2.Normalize(a2).Str());
        Console.WriteLine(Vector2.Lerp(a2, b2, 0.25f).Str());
        Console.WriteLine(Vector2.Min(a2, b2).Str());
        Console.WriteLine(Vector2.Max(a2, b2).Str());
        Console.WriteLine(Vector2.Clamp(new Vector2(5, -5), new Vector2(0, -1), new Vector2(4, 1)).Str());
        Console.WriteLine(Vector2.Abs(b2).Str());
        Console.WriteLine(Vector2.SquareRoot(new Vector2(16, 2.25f)).Str());
        Console.WriteLine(Vector2.Reflect(new Vector2(1, -1), new Vector2(0, 1)).Str());
        Console.WriteLine(Vector2.Negate(a2).Str() + Vector2.Add(a2, b2).Str() + Vector2.Subtract(a2, b2).Str()
            + Vector2.Multiply(a2, b2).Str() + Vector2.Multiply(a2, 3).Str() + Vector2.Multiply(3, a2).Str()
            + Vector2.Divide(a2, b2).Str() + Vector2.Divide(a2, 4).Str());
        Console.WriteLine(a2[0] + " " + a2[1]);
        var m2 = a2;
        m2[1] = 9;
        m2.X = 7;
        Console.WriteLine(m2.Str());
        Try(() => { var _ = a2[2]; });
        Try(() => { var v = a2; v[-1] = 0; });
        Console.WriteLine(a2.Equals(new Vector2(3, 4)) + " " + a2.Equals((object)new Vector2(3, 5)) + " " + a2.Equals("x") + " " + a2.GetHashCode().Equals(new Vector2(3, 4).GetHashCode()));
        var floats = new float[4];
        a2.CopyTo(floats);
        a2.CopyTo(floats, 2);
        Console.WriteLine(string.Join(",", floats));
        Try(() => a2.CopyTo(new float[1]));
        Try(() => a2.CopyTo(floats, 3));
        Try(() => a2.CopyTo(floats, 4));
        Try(() => a2.CopyTo((float[])null!));
        Span<float> span = stackalloc float[3];
        a2.CopyTo(span);
        Console.WriteLine(span[0] + " " + span[1] + " " + span[2] + " " + a2.TryCopyTo(span.Slice(2)) + " " + a2.TryCopyTo(span.Slice(1)));
        Try(() => a2.CopyTo(Span<float>.Empty));
        Console.WriteLine(new Vector2(new float[] { 5, 6, 7 }).Str());
        Try(() => new Vector2(new float[] { 5 }));
        Console.WriteLine(new Vector2(1.5f, -2.25f).ToString("G", comma));
        Console.WriteLine(new Vector2(1.5f, -2.25f).ToString("F2", inv));
        Console.WriteLine(new Vector2(1234.5f, 0.5f).ToString("N1", comma));
        var set = new HashSet<Vector2> { a2, b2, new Vector2(3, 4) };
        Console.WriteLine(set.Count);
        var dict = new Dictionary<Vector2, string> { [a2] = "a" };
        Console.WriteLine(dict[new Vector2(3, 4)]);

        // ---- Vector3
        var a3 = new Vector3(1, 2, 3);
        var b3 = new Vector3(4, -5, 6);
        Console.WriteLine(a3.Str());
        Console.WriteLine(new Vector3(new Vector2(1, 2), 3).Str());
        Console.WriteLine($"{Vector3.Zero.Str()} {Vector3.One.Str()} {Vector3.UnitX.Str()} {Vector3.UnitY.Str()} {Vector3.UnitZ.Str()}");
        Console.WriteLine((a3 + b3).Str());
        Console.WriteLine((a3 - b3).Str());
        Console.WriteLine((a3 * b3).Str());
        Console.WriteLine((a3 * 2).Str() + (2 * a3).Str());
        Console.WriteLine((b3 / a3).Str() + (a3 / 4).Str());
        Console.WriteLine((-a3).Str());
        Console.WriteLine((a3 == b3) + " " + (a3 != b3));
        Console.WriteLine(a3.Length());
        Console.WriteLine(a3.LengthSquared());
        Console.WriteLine(Vector3.Dot(a3, b3));
        Console.WriteLine(Vector3.Cross(a3, b3).Str());
        Console.WriteLine(Vector3.Distance(a3, b3));
        Console.WriteLine(Vector3.DistanceSquared(a3, b3));
        Console.WriteLine(Vector3.Normalize(new Vector3(0, 3, 4)).Str());
        Console.WriteLine(Vector3.Lerp(a3, b3, 0.5f).Str());
        Console.WriteLine(Vector3.Min(a3, b3).Str() + Vector3.Max(a3, b3).Str());
        Console.WriteLine(Vector3.Clamp(new Vector3(9, -9, 2), Vector3.Zero, new Vector3(5, 5, 5)).Str());
        Console.WriteLine(Vector3.Abs(b3).Str() + Vector3.SquareRoot(new Vector3(4, 9, 16)).Str());
        Console.WriteLine(Vector3.Reflect(new Vector3(1, -2, 3), new Vector3(0, 1, 0)).Str());
        Console.WriteLine(a3[0] + " " + a3[1] + " " + a3[2]);
        Try(() => { var _ = a3[3]; });
        Try(() => { var v = a3; v[3] = 0; });
        a3.CopyTo(floats);
        Console.WriteLine(string.Join(",", floats));
        Try(() => a3.CopyTo(floats, 2));
        Console.WriteLine(new Vector3(floats.AsSpan()).Str());
        Try(() => new Vector3(new float[2]));
        Console.WriteLine(a3.ToString("G", comma));
        Console.WriteLine(a3.Equals(new Vector3(1, 2, 3)) + " " + a3.Equals(b3));

        // ---- Vector4
        var a4 = new Vector4(1, 2, 3, 4);
        var b4 = new Vector4(-1, 0.5f, 2, 8);
        Console.WriteLine(a4.Str());
        Console.WriteLine(new Vector4(new Vector3(1, 2, 3), 4).Str() + new Vector4(new Vector2(1, 2), 3, 4).Str() + new Vector4(7).Str());
        Console.WriteLine($"{Vector4.Zero.Str()} {Vector4.One.Str()} {Vector4.UnitX.Str()} {Vector4.UnitW.Str()}");
        Console.WriteLine((a4 + b4).Str());
        Console.WriteLine((a4 - b4).Str());
        Console.WriteLine((a4 * b4).Str());
        Console.WriteLine((a4 * 0.5f).Str() + (3 * a4).Str());
        Console.WriteLine((a4 / b4).Str() + (a4 / 2).Str());
        Console.WriteLine((-a4).Str());
        Console.WriteLine((a4 == b4) + " " + (a4 != b4) + " " + (a4 == new Vector4(1, 2, 3, 4)));
        Console.WriteLine(a4.Length());
        Console.WriteLine(a4.LengthSquared());
        Console.WriteLine(Vector4.Dot(a4, b4));
        Console.WriteLine(Vector4.Distance(a4, b4));
        Console.WriteLine(Vector4.DistanceSquared(a4, b4));
        Console.WriteLine(Vector4.Normalize(new Vector4(2, 0, 0, 0)).Str());
        Console.WriteLine(Vector4.Lerp(a4, b4, 0.25f).Str());
        Console.WriteLine(Vector4.Min(a4, b4).Str() + Vector4.Max(a4, b4).Str());
        Console.WriteLine(Vector4.Clamp(new Vector4(9, -9, 2, 0), Vector4.Zero, new Vector4(5, 5, 5, 5)).Str());
        Console.WriteLine(Vector4.Abs(b4).Str() + Vector4.SquareRoot(new Vector4(4, 9, 16, 25)).Str());
        Console.WriteLine(a4[0] + " " + a4[1] + " " + a4[2] + " " + a4[3]);
        Try(() => { var _ = a4[4]; });
        Try(() => { var v = a4; v[-2] = 0; });
        a4.CopyTo(floats);
        Console.WriteLine(string.Join(",", floats));
        Console.WriteLine(new Vector4(floats).Str());
        Try(() => new Vector4(new float[3]));
        Try(() => a4.CopyTo(floats, 1));
        Console.WriteLine(a4.ToString("G", comma));
        Console.WriteLine(a4.ToString("F1", inv));
        var m4 = a4;
        m4.W = 0;
        m4[2] = -3;
        Console.WriteLine(m4.Str());
        Console.WriteLine(a4.Equals(new Vector4(1, 2, 3, 4)) + " " + a4.Equals(m4));

        // as array elements and fields
        var array = new Vector4[3];
        array[1] = a4;
        array[2].X = 5;
        Console.WriteLine(array[0].Str() + array[1].Str() + array[2].Str());
        object boxed = a2;
        Console.WriteLine(boxed is Vector2 unboxed && unboxed == a2);
        Console.WriteLine(boxed.ToString() == a2.ToString());
    }
}
