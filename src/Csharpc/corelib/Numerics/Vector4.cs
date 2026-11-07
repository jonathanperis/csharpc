// System.Numerics.Vector4 (see Vector2.cs).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Numerics.Vector4")]
    public unsafe struct Vector4Impl : IEquatable<System.Numerics.Vector4>, IFormattable
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Vector4Impl(float value) { X = value; Y = value; Z = value; W = value; }
        public Vector4Impl(float x, float y, float z, float w) { X = x; Y = y; Z = z; W = w; }
        public Vector4Impl(System.Numerics.Vector2 value, float z, float w) { X = value.X; Y = value.Y; Z = z; W = w; }
        public Vector4Impl(System.Numerics.Vector3 value, float w) { X = value.X; Y = value.Y; Z = value.Z; W = w; }

        public Vector4Impl(ReadOnlySpan<float> values)
        {
            if (values.Length < 4) throw new ArgumentOutOfRangeException("values");
            X = values[0];
            Y = values[1];
            Z = values[2];
            W = values[3];
        }

        private static System.Numerics.Vector4 To(Vector4Impl v) => *(System.Numerics.Vector4*)&v;
        private static System.Numerics.Vector4 V(float x, float y, float z, float w) => To(new Vector4Impl(x, y, z, w));
        // (x0 + x1) + (x2 + x3): the horizontal addition of DPPS / FADDP.
        private static float Dot4(System.Numerics.Vector4 a, System.Numerics.Vector4 b) => (a.X * b.X + a.Y * b.Y) + (a.Z * b.Z + a.W * b.W);

        public static System.Numerics.Vector4 Zero => V(0, 0, 0, 0);
        public static System.Numerics.Vector4 One => V(1, 1, 1, 1);
        public static System.Numerics.Vector4 UnitX => V(1, 0, 0, 0);
        public static System.Numerics.Vector4 UnitY => V(0, 1, 0, 0);
        public static System.Numerics.Vector4 UnitZ => V(0, 0, 1, 0);
        public static System.Numerics.Vector4 UnitW => V(0, 0, 0, 1);

        public float this[int index]
        {
            get
            {
                if ((uint)index >= 4) throw new ArgumentOutOfRangeException();
                return index == 0 ? X : index == 1 ? Y : index == 2 ? Z : W;
            }
            set
            {
                if ((uint)index >= 4) throw new ArgumentOutOfRangeException();
                if (index == 0) X = value; else if (index == 1) Y = value; else if (index == 2) Z = value; else W = value;
            }
        }

        public static System.Numerics.Vector4 Abs(System.Numerics.Vector4 value) => V(Math.Abs(value.X), Math.Abs(value.Y), Math.Abs(value.Z), Math.Abs(value.W));
        public static System.Numerics.Vector4 Add(System.Numerics.Vector4 left, System.Numerics.Vector4 right) => V(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        public static System.Numerics.Vector4 Clamp(System.Numerics.Vector4 value1, System.Numerics.Vector4 min, System.Numerics.Vector4 max) => Min(Max(value1, min), max);
        public static float Distance(System.Numerics.Vector4 value1, System.Numerics.Vector4 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(System.Numerics.Vector4 value1, System.Numerics.Vector4 value2) { var d = Subtract(value1, value2); return Dot4(d, d); }
        public static System.Numerics.Vector4 Divide(System.Numerics.Vector4 left, System.Numerics.Vector4 right) => V(left.X / right.X, left.Y / right.Y, left.Z / right.Z, left.W / right.W);
        public static System.Numerics.Vector4 Divide(System.Numerics.Vector4 left, float divisor) => V(left.X / divisor, left.Y / divisor, left.Z / divisor, left.W / divisor);
        public static float Dot(System.Numerics.Vector4 vector1, System.Numerics.Vector4 vector2) => Dot4(vector1, vector2);
        public static System.Numerics.Vector4 Lerp(System.Numerics.Vector4 value1, System.Numerics.Vector4 value2, float amount) => Add(Multiply(value1, 1.0f - amount), Multiply(value2, amount));
        public static System.Numerics.Vector4 Max(System.Numerics.Vector4 value1, System.Numerics.Vector4 value2) =>
            V(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y, value1.Z > value2.Z ? value1.Z : value2.Z, value1.W > value2.W ? value1.W : value2.W);
        public static System.Numerics.Vector4 Min(System.Numerics.Vector4 value1, System.Numerics.Vector4 value2) =>
            V(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y, value1.Z < value2.Z ? value1.Z : value2.Z, value1.W < value2.W ? value1.W : value2.W);
        public static System.Numerics.Vector4 Multiply(System.Numerics.Vector4 left, System.Numerics.Vector4 right) => V(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
        public static System.Numerics.Vector4 Multiply(System.Numerics.Vector4 left, float right) => V(left.X * right, left.Y * right, left.Z * right, left.W * right);
        public static System.Numerics.Vector4 Multiply(float left, System.Numerics.Vector4 right) => V(left * right.X, left * right.Y, left * right.Z, left * right.W);
        public static System.Numerics.Vector4 Negate(System.Numerics.Vector4 value) => V(-value.X, -value.Y, -value.Z, -value.W);
        public static System.Numerics.Vector4 Normalize(System.Numerics.Vector4 vector) => Divide(vector, MathF.Sqrt(Dot4(vector, vector)));
        public static System.Numerics.Vector4 SquareRoot(System.Numerics.Vector4 value) => V(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z), MathF.Sqrt(value.W));
        public static System.Numerics.Vector4 Subtract(System.Numerics.Vector4 left, System.Numerics.Vector4 right) => V(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

        public static Vector4Impl operator +(Vector4Impl left, Vector4Impl right) => new Vector4Impl(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);
        public static Vector4Impl operator -(Vector4Impl left, Vector4Impl right) => new Vector4Impl(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);
        public static Vector4Impl operator -(Vector4Impl value) => new Vector4Impl(-value.X, -value.Y, -value.Z, -value.W);
        public static Vector4Impl operator *(Vector4Impl left, Vector4Impl right) => new Vector4Impl(left.X * right.X, left.Y * right.Y, left.Z * right.Z, left.W * right.W);
        public static Vector4Impl operator *(Vector4Impl left, float right) => new Vector4Impl(left.X * right, left.Y * right, left.Z * right, left.W * right);
        public static Vector4Impl operator *(float left, Vector4Impl right) => new Vector4Impl(left * right.X, left * right.Y, left * right.Z, left * right.W);
        public static Vector4Impl operator /(Vector4Impl left, Vector4Impl right) => new Vector4Impl(left.X / right.X, left.Y / right.Y, left.Z / right.Z, left.W / right.W);
        public static Vector4Impl operator /(Vector4Impl value1, float value2) => new Vector4Impl(value1.X / value2, value1.Y / value2, value1.Z / value2, value1.W / value2);
        public static bool operator ==(Vector4Impl left, Vector4Impl right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z && left.W == right.W;
        public static bool operator !=(Vector4Impl left, Vector4Impl right) => !(left == right);

        // .NET: a null array is a NullReferenceException (no null check), then the same "Destination is too short."
        public void CopyTo(float[] array)
        {
            if (array.Length < 4) throw new ArgumentException("Destination is too short.", "destination");
            array[0] = X;
            array[1] = Y;
            array[2] = Z;
            array[3] = W;
        }

        public void CopyTo(float[] array, int startIndex)
        {
            if ((uint)startIndex >= (uint)array.Length) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            if (array.Length - startIndex < 4) throw new ArgumentException("Destination is too short.", "destination");
            array[startIndex] = X;
            array[startIndex + 1] = Y;
            array[startIndex + 2] = Z;
            array[startIndex + 3] = W;
        }

        public void CopyTo(Span<float> destination)
        {
            if (destination.Length < 4) throw new ArgumentException("Destination is too short.", "destination");
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
            destination[3] = W;
        }

        public bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 4) return false;
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
            destination[3] = W;
            return true;
        }

        public override bool Equals(object obj) => obj is System.Numerics.Vector4 other && Equals(other);
        public bool Equals(System.Numerics.Vector4 other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
        public float Length() => MathF.Sqrt((X * X + Y * Y) + (Z * Z + W * W));
        public float LengthSquared() => (X * X + Y * Y) + (Z * Z + W * W);
        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            return "<" + X.ToString(format, formatProvider) + separator + " " + Y.ToString(format, formatProvider) + separator + " " + Z.ToString(format, formatProvider)
                + separator + " " + W.ToString(format, formatProvider) + ">";
        }
    }
}
