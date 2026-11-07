// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// csharpc: the payload of EventSource.Write<T> (the self-describing events). .NET analyzes T once
// (TraceLoggingTypeInfo, TypeAnalysis) and hands the listeners EventPayload dictionaries; this is the same analysis
// over csharpc's reflection metadata: the public instance properties of an anonymous type or a type marked
// [EventData] (derived type first, then its base types, as reflection lists them), each a scalar (primitives,
// enums, Guid, DateTime, DateTimeOffset, TimeSpan, decimal and their nullable forms, boxed as they are), a string
// (null becomes ""), a nested payload type (an EventPayload dictionary), an array of scalars (the array itself) or
// an array of anything else (an object[] of the converted elements). Failures follow .NET: an unsupported T or
// property type is an ArgumentException when the source is enabled, a scalar or enumerable top-level T an
// ArgumentNullException, string arrays and nested arrays NotSupportedException. What csharpc cannot analyze
// (multi-dimensional arrays, enumerables that are not arrays, recursive types) is an explicit NotSupportedException.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace CsharpcCoreLib
{
    /// <summary>The nested payload of a self-describing event (System.Diagnostics.Tracing.EventPayload): a read-only dictionary of property names to values.</summary>
    [Shadows("System.Diagnostics.Tracing.EventPayload")]
    internal sealed class EventPayloadImpl : IDictionary<string, object>
    {
        private readonly string[] _names;
        private readonly object[] _values;

        internal EventPayloadImpl(string[] names, object[] values) { _names = names; _values = values; }

        public ICollection<string> Keys => _names;
        public ICollection<object> Values => _values;
        public int Count => _names.Length;
        public bool IsReadOnly => true;

        public object this[string key]
        {
            get
            {
                if (key == null) throw new ArgumentNullException("key");
                int i = Array.IndexOf(_names, key);
                if (i < 0) throw new KeyNotFoundException();
                return _values[i];
            }
            set => throw new NotSupportedException();
        }

        public bool ContainsKey(string key)
        {
            if (key == null) throw new ArgumentNullException("key");
            return Array.IndexOf(_names, key) >= 0;
        }

        public bool Contains(KeyValuePair<string, object> entry) => Array.IndexOf(_names, entry.Key) is var i && i >= 0 && Equals(_values[i], entry.Value);

        public bool TryGetValue(string key, out object value)
        {
            if (key == null) throw new ArgumentNullException("key");
            int i = Array.IndexOf(_names, key);
            value = i >= 0 ? _values[i] : null;
            return i >= 0;
        }

        public void Add(string key, object value) => throw new NotSupportedException();
        public void Add(KeyValuePair<string, object> entry) => throw new NotSupportedException();
        public bool Remove(string key) => throw new NotSupportedException();
        public bool Remove(KeyValuePair<string, object> entry) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();

        public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException("array");
            for (int i = 0; i < _names.Length; i++) array[arrayIndex + i] = new KeyValuePair<string, object>(_names[i], _values[i]);
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            for (int i = 0; i < _names.Length; i++) yield return new KeyValuePair<string, object>(_names[i], _values[i]);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class SelfDescribing
    {
        internal enum Kind { Scalar, String, Nested, AsIs, Sequence }

        internal sealed class Field
        {
            internal Kind Kind;
            internal Layout Nested;      // Kind.Nested, or the elements of a Sequence of nested payloads
            internal Kind ElementKind;   // Kind.Sequence
            internal bool AllowNull;     // a nullable payload struct: null stays null
            internal PropertyInfo Property;
        }

        internal sealed class Layout
        {
            internal string[] Names;
            internal Field[] Fields;
            internal bool TopLevelString;  // a string or nullable T: .NET's cast of the value to EventPayload fails
            internal Type Underlying;      // ... of this type (the nullable's)
        }

        private static readonly Dictionary<Type, Layout> s_layouts = new Dictionary<Type, Layout>();
        [ThreadStatic] private static List<Type> t_analyzing;

        private static bool IsScalar(Type t) =>
            t.IsPrimitive || t.IsEnum || t == typeof(decimal) || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(TimeSpan);

        // Arrays of these are handed over as they are; every other array becomes an object[] of converted elements.
        private static bool IsAsIsElement(Type t) =>
            t == typeof(bool) || t == typeof(byte) || t == typeof(sbyte) || t == typeof(char) || t == typeof(short) || t == typeof(ushort)
            || t == typeof(int) || t == typeof(uint) || t == typeof(long) || t == typeof(ulong) || t == typeof(IntPtr) || t == typeof(UIntPtr)
            || t == typeof(float) || t == typeof(double) || t == typeof(Guid);

        // Anonymous types are named by the compiler; the framework's own types are never [EventData] (and their
        // attribute lists are not what csharpc keeps metadata for).
        private static bool IsPayloadType(Type t)
        {
            if (t.Name.Contains("AnonymousType")) return true;
            if (t.IsPrimitive || t.IsEnum || t.IsArray || (t.Namespace != null && (t.Namespace == "System" || t.Namespace.StartsWith("System.", StringComparison.Ordinal)))) return false;
            return t.IsDefined(typeof(EventDataAttribute), false);
        }

        private static ArgumentException NonCompliant(Type t) =>
            new ArgumentException("The API supports only anonymous types or types decorated with the EventDataAttribute. Non-compliant type: " + t.Name + " dataType.");

        /// <summary>TraceLoggingTypeInfo.GetInstance for the top-level T of Write&lt;T&gt;.</summary>
        internal static Layout LayoutOf(Type t)
        {
            if (!IsPayloadType(t))
            {
                if (t == typeof(string)) return new Layout { Names = new string[0], Fields = new Field[0], TopLevelString = true, Underlying = t };
                Type underlying = Nullable.GetUnderlyingType(t);
                if (underlying != null && IsScalar(underlying)) return new Layout { Names = new string[0], Fields = new Field[0], TopLevelString = true, Underlying = underlying };
                if (IsScalar(t) || t.IsArray || typeof(IEnumerable).IsAssignableFrom(t))
                    throw new ArgumentNullException("name", "This usually means that the object passed to Write is of a type that does not support being used as the top-level object in an event, e.g. a primitive or built-in type.");
                throw NonCompliant(t);
            }
            return Analyze(t);
        }

        private static Layout Analyze(Type t)
        {
            lock (s_layouts)
                if (s_layouts.TryGetValue(t, out Layout known)) return known;
            t_analyzing ??= new List<Type>();
            if (t_analyzing.Contains(t))
                throw new NotSupportedException("EventSource.Write<T> with the recursive payload type " + t.Name + " is not supported by csharpc yet.");
            t_analyzing.Add(t);
            try
            {
                var names = new List<string>();
                var fields = new List<Field>();
                foreach (PropertyInfo property in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length > 0) continue;
                    MethodInfo getter = property.GetGetMethod();
                    if (getter == null || getter.IsStatic || !getter.IsPublic) continue;
                    if (property.IsDefined(typeof(EventIgnoreAttribute), false)) continue;
                    Field field = FieldOf(property.PropertyType);
                    field.Property = property;
                    names.Add(property.Name);
                    fields.Add(field);
                }
                var layout = new Layout { Names = names.ToArray(), Fields = fields.ToArray() };
                lock (s_layouts) s_layouts[t] = layout;
                return layout;
            }
            finally { t_analyzing.Remove(t); }
        }

        private static Field FieldOf(Type t)
        {
            if (t == typeof(string)) return new Field { Kind = Kind.String };
            if (IsScalar(t)) return new Field { Kind = Kind.Scalar };
            Type underlying = Nullable.GetUnderlyingType(t);
            if (underlying != null)
            {
                if (IsScalar(underlying)) return new Field { Kind = Kind.Scalar };
                if (IsPayloadType(underlying)) return new Field { Kind = Kind.Nested, Nested = Analyze(underlying), AllowNull = true };
                throw NonCompliant(t);
            }
            if (t.IsArray)
            {
                if (t.GetArrayRank() != 1)
                    throw new NotSupportedException("EventSource.Write<T> with a multi-dimensional array property is not supported by csharpc yet.");
                Type element = t.GetElementType();
                if (IsAsIsElement(element)) return new Field { Kind = Kind.AsIs };
                if (element == typeof(string)) throw new NotSupportedException("Arrays of null-terminated string are not supported.");
                if (element.IsArray || (typeof(IEnumerable).IsAssignableFrom(element) && element != typeof(string)))
                    throw new NotSupportedException("Nested arrays/enumerables are not supported.");
                Field inner = FieldOf(element);
                return new Field { Kind = Kind.Sequence, ElementKind = inner.Kind, Nested = inner.Nested, AllowNull = inner.AllowNull };
            }
            if (IsPayloadType(t)) return new Field { Kind = Kind.Nested, Nested = Analyze(t) };
            if (typeof(IEnumerable).IsAssignableFrom(t))
                throw new NotSupportedException("EventSource.Write<T> with the enumerable property type " + t.Name + " is not supported by csharpc yet (arrays are).");
            throw NonCompliant(t);
        }

        /// <summary>The names and values of <paramref name="data"/>, or the exception .NET reports as an EventSourceException message.</summary>
        internal static object[] Read(Layout layout, object data, string eventName, out string[] names)
        {
            if (layout.TopLevelString)
            {
                // A string (null too) or a nullable with a value can't be an EventPayload; a null nullable is an event without data.
                if (layout.Underlying == typeof(string) || data != null)
                    throw new InvalidCastException("Unable to cast object of type '" + layout.Underlying.FullName + "' to type 'System.Diagnostics.Tracing.EventPayload'.");
                names = null;
                return null;
            }
            if (data == null) throw new NullReferenceException();
            names = layout.Names;
            return ReadFields(layout, data);
        }

        private static object[] ReadFields(Layout layout, object data)
        {
            var values = new object[layout.Fields.Length];
            for (int i = 0; i < values.Length; i++)
            {
                Field field = layout.Fields[i];
                object value;
                // .NET calls the getters through compiled delegates: their exceptions arrive as they are.
                try { value = field.Property.GetValue(data); }
                catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
                values[i] = Convert(field, field.Kind, value);
            }
            return values;
        }

        private static object Convert(Field field, Kind kind, object value)
        {
            switch (kind)
            {
                case Kind.String: return value ?? "";
                case Kind.Nested:
                    if (value == null)
                    {
                        if (field.AllowNull) return null;
                        throw new NullReferenceException();
                    }
                    return new EventPayloadImpl(field.Nested.Names, ReadFields(field.Nested, value));
                case Kind.Sequence:
                    if (value == null) return null;
                    var array = (Array)value;
                    var elements = new object[array.Length];
                    for (int i = 0; i < elements.Length; i++) elements[i] = Convert(field, field.ElementKind, array.GetValue(i));
                    return elements;
                default: return value;
            }
        }
    }
}
