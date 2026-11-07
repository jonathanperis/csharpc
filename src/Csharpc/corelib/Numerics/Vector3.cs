// System.Numerics.Vector3 (see Vector2.cs).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Numerics.Vector3")]
    public unsafe struct Vector3Impl : IEquatable<System.Numerics.Vector3>, IFormattable
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Impl(float value) { X = value; Y = value; Z = value; }
        public Vector3Impl(System.Numerics.Vector2 value, float z) { X = value.X; Y = value.Y; Z = z; }
        public Vector3Impl(float x, float y, float z) { X = x; Y = y; Z = z; }

        public Vector3Impl(ReadOnlySpan<float> values)
        {
            if (values.Length < 3) throw new ArgumentOutOfRangeException("values");
            X = values[0];
            Y = values[1];
            Z = values[2];
        }

        private static System.Numerics.Vector3 To(Vector3Impl v) => *(System.Numerics.Vector3*)&v;
        private static System.Numerics.Vector3 V(float x, float y, float z) => To(new Vector3Impl(x, y, z));
        // (x0 + x1) + (x2 + 0): the fourth lane of a 128-bit register is zero.
        private static float Dot3(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => (a.X * b.X + a.Y * b.Y) + a.Z * b.Z;

        public static System.Numerics.Vector3 Zero => V(0, 0, 0);
        public static System.Numerics.Vector3 One => V(1, 1, 1);
        public static System.Numerics.Vector3 UnitX => V(1, 0, 0);
        public static System.Numerics.Vector3 UnitY => V(0, 1, 0);
        public static System.Numerics.Vector3 UnitZ => V(0, 0, 1);

        public float this[int index]
        {
            get
            {
                if ((uint)index >= 3) throw new ArgumentOutOfRangeException("index");
                return index == 0 ? X : index == 1 ? Y : Z;
            }
            set
            {
                if ((uint)index >= 3) throw new ArgumentOutOfRangeException("index");
                if (index == 0) X = value; else if (index == 1) Y = value; else Z = value;
            }
        }

        public static System.Numerics.Vector3 Abs(System.Numerics.Vector3 value) => V(Math.Abs(value.X), Math.Abs(value.Y), Math.Abs(value.Z));
        public static System.Numerics.Vector3 Add(System.Numerics.Vector3 left, System.Numerics.Vector3 right) => V(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static System.Numerics.Vector3 Clamp(System.Numerics.Vector3 value1, System.Numerics.Vector3 min, System.Numerics.Vector3 max) => Min(Max(value1, min), max);
        public static System.Numerics.Vector3 Cross(System.Numerics.Vector3 vector1, System.Numerics.Vector3 vector2) =>
            V(vector1.Y * vector2.Z - vector1.Z * vector2.Y, vector1.Z * vector2.X - vector1.X * vector2.Z, vector1.X * vector2.Y - vector1.Y * vector2.X);
        public static float Distance(System.Numerics.Vector3 value1, System.Numerics.Vector3 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(System.Numerics.Vector3 value1, System.Numerics.Vector3 value2) { var d = Subtract(value1, value2); return Dot3(d, d); }
        public static System.Numerics.Vector3 Divide(System.Numerics.Vector3 left, System.Numerics.Vector3 right) => V(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static System.Numerics.Vector3 Divide(System.Numerics.Vector3 left, float divisor) => V(left.X / divisor, left.Y / divisor, left.Z / divisor);
        public static float Dot(System.Numerics.Vector3 vector1, System.Numerics.Vector3 vector2) => Dot3(vector1, vector2);
        public static System.Numerics.Vector3 Lerp(System.Numerics.Vector3 value1, System.Numerics.Vector3 value2, float amount) => Add(Multiply(value1, 1.0f - amount), Multiply(value2, amount));
        public static System.Numerics.Vector3 Max(System.Numerics.Vector3 value1, System.Numerics.Vector3 value2) =>
            V(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y, value1.Z > value2.Z ? value1.Z : value2.Z);
        public static System.Numerics.Vector3 Min(System.Numerics.Vector3 value1, System.Numerics.Vector3 value2) =>
            V(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y, value1.Z < value2.Z ? value1.Z : value2.Z);
        public static System.Numerics.Vector3 Multiply(System.Numerics.Vector3 left, System.Numerics.Vector3 right) => V(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static System.Numerics.Vector3 Multiply(System.Numerics.Vector3 left, float right) => V(left.X * right, left.Y * right, left.Z * right);
        public static System.Numerics.Vector3 Multiply(float left, System.Numerics.Vector3 right) => V(left * right.X, left * right.Y, left * right.Z);
        public static System.Numerics.Vector3 Negate(System.Numerics.Vector3 value) => V(-value.X, -value.Y, -value.Z);
        public static System.Numerics.Vector3 Normalize(System.Numerics.Vector3 value) => Divide(value, MathF.Sqrt(Dot3(value, value)));
        public static System.Numerics.Vector3 Reflect(System.Numerics.Vector3 vector, System.Numerics.Vector3 normal)
        {
            float dot = Dot3(vector, normal);
            return Subtract(vector, Multiply(2.0f * dot, normal));
        }
        public static System.Numerics.Vector3 SquareRoot(System.Numerics.Vector3 value) => V(MathF.Sqrt(value.X), MathF.Sqrt(value.Y), MathF.Sqrt(value.Z));
        public static System.Numerics.Vector3 Subtract(System.Numerics.Vector3 left, System.Numerics.Vector3 right) => V(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        public static Vector3Impl operator +(Vector3Impl left, Vector3Impl right) => new Vector3Impl(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3Impl operator -(Vector3Impl left, Vector3Impl right) => new Vector3Impl(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3Impl operator -(Vector3Impl value) => new Vector3Impl(-value.X, -value.Y, -value.Z);
        public static Vector3Impl operator *(Vector3Impl left, Vector3Impl right) => new Vector3Impl(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static Vector3Impl operator *(Vector3Impl left, float right) => new Vector3Impl(left.X * right, left.Y * right, left.Z * right);
        public static Vector3Impl operator *(float left, Vector3Impl right) => new Vector3Impl(left * right.X, left * right.Y, left * right.Z);
        public static Vector3Impl operator /(Vector3Impl left, Vector3Impl right) => new Vector3Impl(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static Vector3Impl operator /(Vector3Impl value1, float value2) => new Vector3Impl(value1.X / value2, value1.Y / value2, value1.Z / value2);
        public static bool operator ==(Vector3Impl left, Vector3Impl right) => left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        public static bool operator !=(Vector3Impl left, Vector3Impl right) => !(left == right);

        // .NET: a null array is a NullReferenceException (no null check), then the same "Destination is too short."
        public void CopyTo(float[] array)
        {
            if (array.Length < 3) throw new ArgumentException("Destination is too short.", "destination");
            array[0] = X;
            array[1] = Y;
            array[2] = Z;
        }

        public void CopyTo(float[] array, int startIndex)
        {
            if ((uint)startIndex >= (uint)array.Length) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            if (array.Length - startIndex < 3) throw new ArgumentException("Destination is too short.", "destination");
            array[startIndex] = X;
            array[startIndex + 1] = Y;
            array[startIndex + 2] = Z;
        }

        public void CopyTo(Span<float> destination)
        {
            if (destination.Length < 3) throw new ArgumentException("Destination is too short.", "destination");
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
        }

        public bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 3) return false;
            destination[0] = X;
            destination[1] = Y;
            destination[2] = Z;
            return true;
        }

        public override bool Equals(object obj) => obj is System.Numerics.Vector3 other && Equals(other);
        public bool Equals(System.Numerics.Vector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public float Length() => MathF.Sqrt((X * X + Y * Y) + Z * Z);
        public float LengthSquared() => (X * X + Y * Y) + Z * Z;
        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            return "<" + X.ToString(format, formatProvider) + separator + " " + Y.ToString(format, formatProvider) + separator + " " + Z.ToString(format, formatProvider) + ">";
        }
    }
}
