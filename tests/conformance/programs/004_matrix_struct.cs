var m = new Matrix2(1, 2, 3, 4);
var r = m * m;
Console.WriteLine(r);
Console.WriteLine(m.Determinant);
readonly struct Matrix2(double a, double b, double c, double d)
{
    public double A => a; public double B => b; public double C => c; public double D => d;
    public double Determinant => a * d - b * c;
    public static Matrix2 operator *(Matrix2 x, Matrix2 y) => new(x.A * y.A + x.B * y.C, x.A * y.B + x.B * y.D, x.C * y.A + x.D * y.C, x.C * y.B + x.D * y.D);
    public override string ToString() => $"[{A} {B}; {C} {D}]";
}
