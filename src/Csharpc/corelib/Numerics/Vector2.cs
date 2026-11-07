// System.Numerics.Vector2 / Vector3 / Vector4 as plain float structs with scalar code, after dotnet/runtime's
// System.Numerics.Vectors (release/11.0; see THIRD-PARTY-NOTICES.md): the classic geometry API (constructors,
// constants, operators, Abs/Add/Clamp/Distance/Dot/Lerp/Max/Min/Multiply/Normalize/Reflect/SquareRoot/Subtract,
// Cross, CopyTo, ToString). .NET evaluates a 4-lane dot product as (x0 + x1) + (x2 + x3) (the horizontal add of
// DPPS / FADDP); so do these. Left out, so that using them is an explicit "no implementation" diagnostic:
// Transform with Matrix3x2/Matrix4x4/Quaternion (those types), the generic-math interfaces and the .NET 9
// element-wise helpers (Sin, Cos, Round, ConditionalSelect...).
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.Numerics.Vector2")]
    public unsafe struct Vector2Impl : IEquatable<System.Numerics.Vector2>, IFormattable
    {
        public float X;
        public float Y;

        public Vector2Impl(float value) { X = value; Y = value; }
        public Vector2Impl(float x, float y) { X = x; Y = y; }

        public Vector2Impl(ReadOnlySpan<float> values)
        {
            if (values.Length < 2) throw new ArgumentOutOfRangeException("values");
            X = values[0];
            Y = values[1];
        }

        private static System.Numerics.Vector2 To(Vector2Impl v) => *(System.Numerics.Vector2*)&v;
        private static System.Numerics.Vector2 V(float x, float y) => To(new Vector2Impl(x, y));
        private static float Dot2(System.Numerics.Vector2 a, System.Numerics.Vector2 b) => a.X * b.X + a.Y * b.Y;

        public static System.Numerics.Vector2 Zero => V(0, 0);
        public static System.Numerics.Vector2 One => V(1, 1);
        public static System.Numerics.Vector2 UnitX => V(1, 0);
        public static System.Numerics.Vector2 UnitY => V(0, 1);

        public float this[int index]
        {
            get
            {
                if ((uint)index >= 2) throw new ArgumentOutOfRangeException("index");
                return index == 0 ? X : Y;
            }
            set
            {
                if ((uint)index >= 2) throw new ArgumentOutOfRangeException("index");
                if (index == 0) X = value; else Y = value;
            }
        }

        public static System.Numerics.Vector2 Abs(System.Numerics.Vector2 value) => V(Math.Abs(value.X), Math.Abs(value.Y));
        public static System.Numerics.Vector2 Add(System.Numerics.Vector2 left, System.Numerics.Vector2 right) => V(left.X + right.X, left.Y + right.Y);
        public static System.Numerics.Vector2 Clamp(System.Numerics.Vector2 value1, System.Numerics.Vector2 min, System.Numerics.Vector2 max) => Min(Max(value1, min), max);
        public static float Distance(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2) => MathF.Sqrt(DistanceSquared(value1, value2));
        public static float DistanceSquared(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2) { var d = Subtract(value1, value2); return Dot2(d, d); }
        public static System.Numerics.Vector2 Divide(System.Numerics.Vector2 left, System.Numerics.Vector2 right) => V(left.X / right.X, left.Y / right.Y);
        public static System.Numerics.Vector2 Divide(System.Numerics.Vector2 left, float divisor) => V(left.X / divisor, left.Y / divisor);
        public static float Dot(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2) => Dot2(value1, value2);
        public static System.Numerics.Vector2 Lerp(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2, float amount) => Add(Multiply(value1, 1.0f - amount), Multiply(value2, amount));
        public static System.Numerics.Vector2 Max(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2) => V(value1.X > value2.X ? value1.X : value2.X, value1.Y > value2.Y ? value1.Y : value2.Y);
        public static System.Numerics.Vector2 Min(System.Numerics.Vector2 value1, System.Numerics.Vector2 value2) => V(value1.X < value2.X ? value1.X : value2.X, value1.Y < value2.Y ? value1.Y : value2.Y);
        public static System.Numerics.Vector2 Multiply(System.Numerics.Vector2 left, System.Numerics.Vector2 right) => V(left.X * right.X, left.Y * right.Y);
        public static System.Numerics.Vector2 Multiply(System.Numerics.Vector2 left, float right) => V(left.X * right, left.Y * right);
        public static System.Numerics.Vector2 Multiply(float left, System.Numerics.Vector2 right) => V(left * right.X, left * right.Y);
        public static System.Numerics.Vector2 Negate(System.Numerics.Vector2 value) => V(-value.X, -value.Y);
        public static System.Numerics.Vector2 Normalize(System.Numerics.Vector2 value) => Divide(value, MathF.Sqrt(Dot2(value, value)));
        public static System.Numerics.Vector2 Reflect(System.Numerics.Vector2 vector, System.Numerics.Vector2 normal)
        {
            float dot = Dot2(vector, normal);
            return Subtract(vector, Multiply(2.0f * dot, normal));
        }
        public static System.Numerics.Vector2 SquareRoot(System.Numerics.Vector2 value) => V(MathF.Sqrt(value.X), MathF.Sqrt(value.Y));
        public static System.Numerics.Vector2 Subtract(System.Numerics.Vector2 left, System.Numerics.Vector2 right) => V(left.X - right.X, left.Y - right.Y);

        public static Vector2Impl operator +(Vector2Impl left, Vector2Impl right) => new Vector2Impl(left.X + right.X, left.Y + right.Y);
        public static Vector2Impl operator -(Vector2Impl left, Vector2Impl right) => new Vector2Impl(left.X - right.X, left.Y - right.Y);
        public static Vector2Impl operator -(Vector2Impl value) => new Vector2Impl(-value.X, -value.Y);
        public static Vector2Impl operator *(Vector2Impl left, Vector2Impl right) => new Vector2Impl(left.X * right.X, left.Y * right.Y);
        public static Vector2Impl operator *(Vector2Impl left, float right) => new Vector2Impl(left.X * right, left.Y * right);
        public static Vector2Impl operator *(float left, Vector2Impl right) => new Vector2Impl(left * right.X, left * right.Y);
        public static Vector2Impl operator /(Vector2Impl left, Vector2Impl right) => new Vector2Impl(left.X / right.X, left.Y / right.Y);
        public static Vector2Impl operator /(Vector2Impl value1, float value2) => new Vector2Impl(value1.X / value2, value1.Y / value2);
        public static bool operator ==(Vector2Impl left, Vector2Impl right) => left.X == right.X && left.Y == right.Y;
        public static bool operator !=(Vector2Impl left, Vector2Impl right) => !(left == right);

        // .NET: a null array is a NullReferenceException (no null check), then the same "Destination is too short."
        public void CopyTo(float[] array)
        {
            if (array.Length < 2) throw new ArgumentException("Destination is too short.", "destination");
            array[0] = X;
            array[1] = Y;
        }

        public void CopyTo(float[] array, int startIndex)
        {
            if ((uint)startIndex >= (uint)array.Length) throw new ArgumentOutOfRangeException("startIndex", "Index was out of range. Must be non-negative and less than the size of the collection.");
            if (array.Length - startIndex < 2) throw new ArgumentException("Destination is too short.", "destination");
            array[startIndex] = X;
            array[startIndex + 1] = Y;
        }

        public void CopyTo(Span<float> destination)
        {
            if (destination.Length < 2) throw new ArgumentException("Destination is too short.", "destination");
            destination[0] = X;
            destination[1] = Y;
        }

        public bool TryCopyTo(Span<float> destination)
        {
            if (destination.Length < 2) return false;
            destination[0] = X;
            destination[1] = Y;
            return true;
        }

        public override bool Equals(object obj) => obj is System.Numerics.Vector2 other && Equals(other);
        public bool Equals(System.Numerics.Vector2 other) => X == other.X && Y == other.Y;
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public float Length() => MathF.Sqrt(X * X + Y * Y);
        public float LengthSquared() => X * X + Y * Y;
        public override string ToString() => ToString("G", CultureInfo.CurrentCulture);
        public string ToString(string format) => ToString(format, CultureInfo.CurrentCulture);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            string separator = NumberFormatInfo.GetInstance(formatProvider).NumberGroupSeparator;
            return "<" + X.ToString(format, formatProvider) + separator + " " + Y.ToString(format, formatProvider) + ">";
        }
    }
}
