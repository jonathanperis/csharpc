// System.Text.Json.Nodes: JsonNode, JsonObject, JsonArray, JsonValue, JsonNodeOptions.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Nodes (release/11.0):
// parsed nodes stay backed by their JsonElement until first modified (so WriteTo reproduces the input),
// paths, parents, conversions and exception messages follow .NET.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.Nodes.JsonNodeOptions")]
    public struct JsonNodeOptionsImpl
    {
        public bool PropertyNameCaseInsensitive { get; set; }
    }

    [Shadows("System.Text.Json.Nodes.JsonNode")]
    public abstract class JsonNodeImpl
    {
        private JsonNode _parent;
        private JsonNodeOptions? _options;

        internal JsonNodeImpl(JsonNodeOptions? options = null)
        {
            _options = options;
        }

        internal virtual JsonElementImpl? UnderlyingElement => null;

        internal static JsonNodeImpl Impl(JsonNode node) => (JsonNodeImpl)(object)node;

        internal static JsonNode Real(JsonNodeImpl node) => (JsonNode)(object)node;

        internal JsonNode Self => (JsonNode)(object)this;

        public JsonNodeOptions? Options
        {
            get
            {
                if (!_options.HasValue && _parent != null) _options = Impl(_parent).Options;
                return _options;
            }
        }

        public JsonArrayImpl AsArray()
        {
            JsonArrayImpl array = this as JsonArrayImpl;
            if (array == null) throw new InvalidOperationException("The node must be of type 'JsonArray'.");
            return array;
        }

        public JsonObjectImpl AsObject()
        {
            JsonObjectImpl obj = this as JsonObjectImpl;
            if (obj == null) throw new InvalidOperationException("The node must be of type 'JsonObject'.");
            return obj;
        }

        public JsonValueImpl AsValue()
        {
            JsonValueImpl value = this as JsonValueImpl;
            if (value == null) throw new InvalidOperationException("The node must be of type 'JsonValue'.");
            return value;
        }

        public JsonNode Parent
        {
            get => _parent;
            internal set => _parent = value;
        }

        public string GetPath()
        {
            if (_parent == null) return "$";
            StringBuilder path = new StringBuilder("$");
            Impl(_parent).AppendPath(path, Self);
            return path.ToString();
        }

        internal abstract void AppendPath(StringBuilder path, JsonNode child);

        public JsonNode Root
        {
            get
            {
                JsonNode parent = _parent;
                if (parent == null) return Self;
                while (Impl(parent)._parent != null) parent = Impl(parent)._parent;
                return parent;
            }
        }

        // Not virtual (as in .NET) on purpose: generic virtual methods overridden in generic classes
        // can't be dispatched yet, so the value kinds are switched on here.
        public T GetValue<T>()
        {
            if (this is JsonValueImpl value) return value.GetValueCore<T>();
            throw new InvalidOperationException("The node must be of type 'JsonValue'.");
        }

        public JsonNode this[int index]
        {
            get => GetItem(index);
            set => SetItem(index, value);
        }

        internal virtual JsonNode GetItem(int index) => throw new InvalidOperationException("The node must be of type 'JsonArray', JsonObject.");

        internal virtual void SetItem(int index, JsonNode node) => throw new InvalidOperationException("The node must be of type 'JsonArray', JsonObject.");

        public JsonNode this[string propertyName]
        {
            get => AsObject().GetItem(propertyName);
            set => AsObject().SetItem(propertyName, value);
        }

        public JsonNode DeepClone() => DeepCloneCore();

        internal abstract JsonNode DeepCloneCore();

        public JsonValueKind GetValueKind() => GetValueKindCore();

        internal abstract JsonValueKind GetValueKindCore();

        public string GetPropertyName()
        {
            JsonObjectImpl parentObject = _parent == null ? null : Impl(_parent) as JsonObjectImpl;
            if (parentObject == null) throw new InvalidOperationException("The node must have a parent node of type 'JsonObject'.");
            return parentObject.GetPropertyName(Self);
        }

        public int GetElementIndex()
        {
            JsonArrayImpl parentArray = _parent == null ? null : Impl(_parent) as JsonArrayImpl;
            if (parentArray == null) throw new InvalidOperationException("The node must have a parent node of type 'JsonArray'.");
            return parentArray.IndexOf(Self);
        }

        public static bool DeepEquals(JsonNodeImpl node1, JsonNodeImpl node2)
        {
            if (node1 == null) return node2 == null;
            if (node2 == null) return false;
            return node1.DeepEqualsCore(node2);
        }

        internal abstract bool DeepEqualsCore(JsonNodeImpl node);

        public void ReplaceWith<T>(T value)
        {
            if (_parent == null) return;
            JsonNodeImpl parent = Impl(_parent);
            if (parent is JsonObjectImpl obj) obj.SetItem(GetPropertyName(), ConvertFromValue(value, null));
            else if (parent is JsonArrayImpl array) array.SetItem(GetElementIndex(), ConvertFromValue(value, null));
        }

        internal void AssignParent(JsonNode parent)
        {
            if (_parent != null) throw new InvalidOperationException("The node already has a parent.");
            JsonNode p = parent;
            while (p != null)
            {
                if (ReferenceEquals(p, this)) throw new InvalidOperationException("A node cycle was detected.");
                p = Impl(p)._parent;
            }
            _parent = parent;
        }

        internal static JsonNode ConvertFromValue<T>(T value, JsonNodeOptions? options)
        {
            if (value == null) return null;
            object boxed = value;
            if (boxed is JsonNode node) return node;
            if (boxed is JsonElement element) return CreateNode((JsonElementImpl)(object)element, options);
            return Real(JsonValueImpl.CreateWithDefaultMetadata(value, options));
        }

        // ---------------------------------------------------------------- serialization

        public string ToJsonString(JsonSerializerOptionsImpl options = null)
        {
            JsonWriterOptionsImpl writerOptions = options == null ? default : options.GetWriterOptions();
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(writerOptions);
            WriteTo(writer, options);
            return writer.WrittenString();
        }

        public override string ToString()
        {
            if (this is JsonValueImpl value && GetValueKindCore() == JsonValueKind.String && value.TryGetValue(out string s)) return s;
            JsonWriterOptionsImpl writerOptions = new JsonWriterOptionsImpl();
            writerOptions.Indented = true;
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(writerOptions);
            WriteTo(writer, null);
            return writer.WrittenString();
        }

        public abstract void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null);

        // ---------------------------------------------------------------- parsing

        internal static JsonNode CreateNode(JsonElementImpl element, JsonNodeOptions? options)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.Object: return Real(new JsonObjectImpl(element, options));
                case JsonValueKind.Array: return Real(new JsonArrayImpl(element, options));
                default: return Real(new JsonValueOfElement(element, options));
            }
        }

        public static JsonNodeImpl Parse(ref Utf8JsonReaderImpl reader, JsonNodeOptions? nodeOptions = null) =>
            Impl(CreateNode(JsonElementImpl.ParseValue(ref reader), nodeOptions));

        public static JsonNodeImpl Parse(string json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptionsImpl documentOptions = default)
        {
            if (json == null) throw new ArgumentNullException("json");
            return Impl(CreateNode(JsonElementImpl.Parse(json, documentOptions), nodeOptions));
        }

        public static JsonNodeImpl Parse(ReadOnlySpan<byte> utf8Json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptionsImpl documentOptions = default) =>
            Impl(CreateNode(JsonElementImpl.Parse(utf8Json, documentOptions), nodeOptions));

        public static JsonNodeImpl Parse(Stream utf8Json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptionsImpl documentOptions = default)
        {
            if (utf8Json == null) throw new ArgumentNullException("utf8Json");
            return Impl(CreateNode(JsonDocumentImpl.ParseValue(JsonDocumentImpl.ReadToEnd(utf8Json), documentOptions).RootElement, nodeOptions));
        }

        public static Task<JsonNodeImpl> ParseAsync(Stream utf8Json, JsonNodeOptions? nodeOptions = null, JsonDocumentOptionsImpl documentOptions = default, CancellationToken cancellationToken = default) =>
            Task.FromResult(Parse(utf8Json, nodeOptions, documentOptions));

        internal static JsonNodeImpl Parse(byte[] utf8Json, JsonNodeOptions? nodeOptions) =>
            Impl(CreateNode(JsonDocumentImpl.ParseValue(utf8Json, default).RootElement, nodeOptions));

        /// <summary>The serializer's JsonNode converter: elements become lazily materialized nodes.</summary>
        internal static object ParseValue(ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options) =>
            CreateNode(JsonElementImpl.ParseValue(ref reader), NodeOptions(options));

        internal static JsonNodeOptions? NodeOptions(JsonSerializerOptionsImpl options)
        {
            JsonNodeOptions nodeOptions = new JsonNodeOptions();
            nodeOptions.PropertyNameCaseInsensitive = options.PropertyNameCaseInsensitive;
            return nodeOptions;
        }

        // ---------------------------------------------------------------- operators

        public static implicit operator JsonNodeImpl(bool value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(bool? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(byte value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(byte? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(char value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(char? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(DateTime value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(DateTime? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(DateTimeOffset value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(DateTimeOffset? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(decimal value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(decimal? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(double value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(double? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(short value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(short? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(int value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(int? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(long value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(long? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(sbyte value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(sbyte? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(float value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(float? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(string value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(ushort value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(ushort? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(uint value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(uint? value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(ulong value) => JsonValueImpl.Create(value, null);
        public static implicit operator JsonNodeImpl(ulong? value) => JsonValueImpl.Create(value, null);

        public static explicit operator bool(JsonNodeImpl value) => value.GetValue<bool>();
        public static explicit operator bool?(JsonNodeImpl value) => value?.GetValue<bool>();
        public static explicit operator byte(JsonNodeImpl value) => value.GetValue<byte>();
        public static explicit operator byte?(JsonNodeImpl value) => value?.GetValue<byte>();
        public static explicit operator char(JsonNodeImpl value) => value.GetValue<char>();
        public static explicit operator char?(JsonNodeImpl value) => value?.GetValue<char>();
        public static explicit operator DateTime(JsonNodeImpl value) => value.GetValue<DateTime>();
        public static explicit operator DateTime?(JsonNodeImpl value) => value?.GetValue<DateTime>();
        public static explicit operator DateTimeOffset(JsonNodeImpl value) => value.GetValue<DateTimeOffset>();
        public static explicit operator DateTimeOffset?(JsonNodeImpl value) => value?.GetValue<DateTimeOffset>();
        public static explicit operator decimal(JsonNodeImpl value) => value.GetValue<decimal>();
        public static explicit operator decimal?(JsonNodeImpl value) => value?.GetValue<decimal>();
        public static explicit operator double(JsonNodeImpl value) => value.GetValue<double>();
        public static explicit operator double?(JsonNodeImpl value) => value?.GetValue<double>();
        public static explicit operator short(JsonNodeImpl value) => value.GetValue<short>();
        public static explicit operator short?(JsonNodeImpl value) => value?.GetValue<short>();
        public static explicit operator int(JsonNodeImpl value) => value.GetValue<int>();
        public static explicit operator int?(JsonNodeImpl value) => value?.GetValue<int>();
        public static explicit operator long(JsonNodeImpl value) => value.GetValue<long>();
        public static explicit operator long?(JsonNodeImpl value) => value?.GetValue<long>();
        public static explicit operator sbyte(JsonNodeImpl value) => value.GetValue<sbyte>();
        public static explicit operator sbyte?(JsonNodeImpl value) => value?.GetValue<sbyte>();
        public static explicit operator float(JsonNodeImpl value) => value.GetValue<float>();
        public static explicit operator float?(JsonNodeImpl value) => value?.GetValue<float>();
        public static explicit operator string(JsonNodeImpl value) => value?.GetValue<string>();
        public static explicit operator ushort(JsonNodeImpl value) => value.GetValue<ushort>();
        public static explicit operator ushort?(JsonNodeImpl value) => value?.GetValue<ushort>();
        public static explicit operator uint(JsonNodeImpl value) => value.GetValue<uint>();
        public static explicit operator uint?(JsonNodeImpl value) => value?.GetValue<uint>();
        public static explicit operator ulong(JsonNodeImpl value) => value.GetValue<ulong>();
        public static explicit operator ulong?(JsonNodeImpl value) => value?.GetValue<ulong>();

        internal static void AppendPropertyName(StringBuilder path, string propertyName) => JsonPathHelpers.AppendPropertyName(path, propertyName);
    }

    [Shadows("System.Text.Json.Nodes.JsonObject")]
    public sealed class JsonObjectImpl : JsonNodeImpl, IDictionary<string, JsonNode>, IList<KeyValuePair<string, JsonNode>>
    {
        private JsonElementImpl? _jsonElement;
        private List<KeyValuePair<string, JsonNode>> _items;
        private Dictionary<string, int> _index;

        public JsonObjectImpl(JsonNodeOptions? options = null) : base(options) { }

        public JsonObjectImpl(IEnumerable<KeyValuePair<string, JsonNode>> properties, JsonNodeOptions? options = null) : this(options)
        {
            InitializeEmpty();
            foreach (KeyValuePair<string, JsonNode> property in properties) Add(property.Key, property.Value);
        }

        internal JsonObjectImpl(JsonElementImpl element, JsonNodeOptions? options = null) : this(options)
        {
            _jsonElement = element;
        }

        internal override JsonElementImpl? UnderlyingElement => _jsonElement;

        public static JsonObjectImpl Create(JsonElementImpl element, JsonNodeOptions? options = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.Object: return new JsonObjectImpl(element, options);
                default: throw new InvalidOperationException("The element must be of type 'Object'");
            }
        }

        private void InitializeEmpty()
        {
            _items = new List<KeyValuePair<string, JsonNode>>();
            JsonNodeOptions? options = Options;
            _index = new Dictionary<string, int>(options.HasValue && options.Value.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }

        private List<KeyValuePair<string, JsonNode>> Items
        {
            get
            {
                if (_items == null)
                {
                    InitializeEmpty();
                    if (_jsonElement.HasValue)
                    {
                        foreach (JsonPropertyImpl property in _jsonElement.Value.EnumerateObject())
                        {
                            JsonNode node = CreateNode(property.Value, Options);
                            if (node != null) Impl(node).Parent = Self;
                            AddCore(property.Name, node);
                        }
                    }
                    _jsonElement = null;
                }
                return _items;
            }
        }

        private void AddCore(string name, JsonNode value)
        {
            if (_index.ContainsKey(name)) throw new ArgumentException("An item with the same key has already been added. Key: " + name, "key");
            _index[name] = _items.Count;
            _items.Add(new KeyValuePair<string, JsonNode>(name, value));
        }

        private void Reindex()
        {
            _index.Clear();
            for (int i = 0; i < _items.Count; i++) _index[_items[i].Key] = i;
        }

        internal override JsonNode GetItem(int index) => GetAt(index).Value;

        internal override void SetItem(int index, JsonNode value) => SetAt(index, value);

        internal override JsonNode DeepCloneCore()
        {
            if (_items == null)
                return _jsonElement.HasValue ? Real(new JsonObjectImpl(_jsonElement.Value.Clone(), Options)) : Real(new JsonObjectImpl(Options));
            JsonObjectImpl clone = new JsonObjectImpl(Options);
            clone.InitializeEmpty();
            foreach (KeyValuePair<string, JsonNode> item in _items)
                clone.Add(item.Key, item.Value == null ? null : Impl(item.Value).DeepCloneCore());
            return Real(clone);
        }

        internal string GetPropertyName(JsonNode node)
        {
            foreach (KeyValuePair<string, JsonNode> item in Items)
                if (ReferenceEquals(item.Value, node)) return item.Key;
            return string.Empty;
        }

        public bool TryGetPropertyValue(string propertyName, out JsonNode jsonNode) => TryGetPropertyValue(propertyName, out jsonNode, out int _);

        public bool TryGetPropertyValue(string propertyName, out JsonNode jsonNode, out int index)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            if (_index.TryGetValue(propertyName, out index))
            {
                jsonNode = items[index].Value;
                return true;
            }
            index = -1;
            jsonNode = null;
            return false;
        }

        public override void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (_items == null && _jsonElement.HasValue)
            {
                _jsonElement.Value.WriteTo(writer);
                return;
            }
            writer.WriteStartObject();
            WriteContentsTo(writer, options);
            writer.WriteEndObject();
        }

        internal void WriteContentsTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options)
        {
            foreach (KeyValuePair<string, JsonNode> entry in Items)
            {
                writer.WritePropertyName(entry.Key);
                if (entry.Value == null) writer.WriteNullValue();
                else Impl(entry.Value).WriteTo(writer, options);
            }
        }

        internal override JsonValueKind GetValueKindCore() => JsonValueKind.Object;

        internal override bool DeepEqualsCore(JsonNodeImpl node)
        {
            switch (node)
            {
                case JsonArrayImpl _:
                    return false;
                case JsonValueImpl value:
                    return value.DeepEqualsCore(this);
                case JsonObjectImpl other:
                    List<KeyValuePair<string, JsonNode>> current = Items;
                    if (current.Count != other.Items.Count) return false;
                    foreach (KeyValuePair<string, JsonNode> item in current)
                    {
                        if (!other.TryGetPropertyValue(item.Key, out JsonNode otherNode)) return false;
                        if (!DeepEquals(item.Value == null ? null : Impl(item.Value), otherNode == null ? null : Impl(otherNode))) return false;
                    }
                    return true;
                default:
                    return false;
            }
        }

        internal JsonNode GetItem(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            return TryGetPropertyValue(propertyName, out JsonNode value) ? value : null;
        }

        internal override void AppendPath(StringBuilder path, JsonNode child)
        {
            if (Parent != null) Impl(Parent).AppendPath(path, Self);
            if (child != null) AppendPropertyName(path, GetPropertyName(child));
        }

        internal void SetItem(string propertyName, JsonNode value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            if (_index.TryGetValue(propertyName, out int index))
            {
                JsonNode replaced = items[index].Value;
                if (ReferenceEquals(value, replaced)) return;
                if (replaced != null) Impl(replaced).Parent = null;
                items[index] = new KeyValuePair<string, JsonNode>(items[index].Key, value);
            }
            else AddCore(propertyName, value);
            if (value != null) Impl(value).AssignParent(Self);
        }

        public void Add(string propertyName, JsonNode value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            AddCore(propertyName, value);
            if (value != null) Impl(value).AssignParent(Self);
        }

        public bool TryAdd(string propertyName, JsonNode value) => TryAdd(propertyName, value, out int _);

        public bool TryAdd(string propertyName, JsonNode value, out int index)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            if (_index.TryGetValue(propertyName, out index)) return false;
            index = items.Count;
            AddCore(propertyName, value);
            if (value != null) Impl(value).AssignParent(Self);
            return true;
        }

        public void Add(KeyValuePair<string, JsonNode> property) => Add(property.Key, property.Value);

        public void Clear()
        {
            if (_items == null)
            {
                _jsonElement = null;
                InitializeEmpty();
                return;
            }
            foreach (KeyValuePair<string, JsonNode> item in _items)
                if (item.Value != null) Impl(item.Value).Parent = null;
            _items.Clear();
            _index.Clear();
        }

        public bool ContainsKey(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            return _index.ContainsKey(propertyName);
        }

        public int Count => Items.Count;

        public bool Remove(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            if (!_index.TryGetValue(propertyName, out int index)) return false;
            JsonNode removed = items[index].Value;
            items.RemoveAt(index);
            Reindex();
            if (removed != null) Impl(removed).Parent = null;
            return true;
        }

        bool ICollection<KeyValuePair<string, JsonNode>>.Contains(KeyValuePair<string, JsonNode> item) =>
            TryGetPropertyValue(item.Key, out JsonNode value) && ReferenceEquals(value, item.Value);

        void ICollection<KeyValuePair<string, JsonNode>>.CopyTo(KeyValuePair<string, JsonNode>[] array, int index) => Items.CopyTo(array, index);

        public IEnumerator<KeyValuePair<string, JsonNode>> GetEnumerator() => Items.GetEnumerator();

        bool ICollection<KeyValuePair<string, JsonNode>>.Remove(KeyValuePair<string, JsonNode> item) => Remove(item.Key);

        ICollection<string> IDictionary<string, JsonNode>.Keys
        {
            get
            {
                List<string> keys = new List<string>();
                foreach (KeyValuePair<string, JsonNode> item in Items) keys.Add(item.Key);
                return keys;
            }
        }

        ICollection<JsonNode> IDictionary<string, JsonNode>.Values
        {
            get
            {
                List<JsonNode> values = new List<JsonNode>();
                foreach (KeyValuePair<string, JsonNode> item in Items) values.Add(item.Value);
                return values;
            }
        }

        bool IDictionary<string, JsonNode>.TryGetValue(string propertyName, out JsonNode jsonNode) => TryGetPropertyValue(propertyName, out jsonNode);

        JsonNode IDictionary<string, JsonNode>.this[string key]
        {
            get
            {
                if (TryGetPropertyValue(key, out JsonNode value)) return value;
                throw new KeyNotFoundException("The given key '" + key + "' was not present in the dictionary.");
            }
            set => SetItem(key, value);
        }

        bool ICollection<KeyValuePair<string, JsonNode>>.IsReadOnly => false;

        IEnumerator IEnumerable.GetEnumerator() => Items.GetEnumerator();

        public KeyValuePair<string, JsonNode> GetAt(int index) => Items[index];

        public void SetAt(int index, string propertyName, JsonNode value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            KeyValuePair<string, JsonNode> existing = items[index];
            if (_index.TryGetValue(propertyName, out int other) && other != index)
                throw new ArgumentException("An item with the same key has already been added. Key: " + propertyName, "key");
            items[index] = new KeyValuePair<string, JsonNode>(propertyName, value);
            Reindex();
            if (existing.Value != null) Impl(existing.Value).Parent = null;
            if (value != null) Impl(value).AssignParent(Self);
        }

        public void SetAt(int index, JsonNode value)
        {
            List<KeyValuePair<string, JsonNode>> items = Items;
            KeyValuePair<string, JsonNode> existing = items[index];
            items[index] = new KeyValuePair<string, JsonNode>(existing.Key, value);
            if (existing.Value != null) Impl(existing.Value).Parent = null;
            if (value != null) Impl(value).AssignParent(Self);
        }

        public int IndexOf(string propertyName)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            return _index.TryGetValue(propertyName, out int index) ? index : -1;
        }

        public void Insert(int index, string propertyName, JsonNode value)
        {
            if (propertyName == null) throw new ArgumentNullException("propertyName");
            List<KeyValuePair<string, JsonNode>> items = Items;
            if (_index.ContainsKey(propertyName)) throw new ArgumentException("An item with the same key has already been added. Key: " + propertyName, "key");
            items.Insert(index, new KeyValuePair<string, JsonNode>(propertyName, value));
            Reindex();
            if (value != null) Impl(value).AssignParent(Self);
        }

        public void RemoveAt(int index)
        {
            List<KeyValuePair<string, JsonNode>> items = Items;
            KeyValuePair<string, JsonNode> existing = items[index];
            items.RemoveAt(index);
            Reindex();
            if (existing.Value != null) Impl(existing.Value).Parent = null;
        }

        KeyValuePair<string, JsonNode> IList<KeyValuePair<string, JsonNode>>.this[int index]
        {
            get => GetAt(index);
            set => SetAt(index, value.Key, value.Value);
        }

        int IList<KeyValuePair<string, JsonNode>>.IndexOf(KeyValuePair<string, JsonNode> item)
        {
            int index = IndexOf(item.Key);
            return index >= 0 && ReferenceEquals(Items[index].Value, item.Value) ? index : -1;
        }

        void IList<KeyValuePair<string, JsonNode>>.Insert(int index, KeyValuePair<string, JsonNode> item) => Insert(index, item.Key, item.Value);

        void IList<KeyValuePair<string, JsonNode>>.RemoveAt(int index) => RemoveAt(index);
    }

    [Shadows("System.Text.Json.Nodes.JsonArray")]
    public sealed class JsonArrayImpl : JsonNodeImpl, IList<JsonNode>
    {
        private JsonElementImpl? _jsonElement;
        private List<JsonNode> _list;

        public JsonArrayImpl(JsonNodeOptions? options = null) : base(options) { }

        public JsonArrayImpl(JsonNodeOptions options, params JsonNode[] items) : base(options) { InitializeFrom(items); }

        public JsonArrayImpl(params JsonNode[] items) : base() { InitializeFrom(items); }

        public JsonArrayImpl(JsonNodeOptions options, ReadOnlySpan<JsonNode> items) : base(options) { InitializeFrom(items.ToArray()); }

        public JsonArrayImpl(ReadOnlySpan<JsonNode> items) : base() { InitializeFrom(items.ToArray()); }

        internal JsonArrayImpl(JsonElementImpl element, JsonNodeOptions? options = null) : base(options)
        {
            _jsonElement = element;
        }

        private void InitializeFrom(JsonNode[] items)
        {
            List<JsonNode> list = new List<JsonNode>(items);
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) Impl(list[i]).AssignParent(Self);
            _list = list;
        }

        internal override JsonElementImpl? UnderlyingElement => _jsonElement;

        public static JsonArrayImpl Create(JsonElementImpl element, JsonNodeOptions? options = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.Array: return new JsonArrayImpl(element, options);
                default: throw new InvalidOperationException("The element must be of type 'Array'");
            }
        }

        private List<JsonNode> List
        {
            get
            {
                if (_list == null)
                {
                    List<JsonNode> list = new List<JsonNode>();
                    if (_jsonElement.HasValue)
                    {
                        foreach (JsonElementImpl element in _jsonElement.Value.EnumerateArray())
                        {
                            JsonNode node = CreateNode(element, Options);
                            if (node != null) Impl(node).AssignParent(Self);
                            list.Add(node);
                        }
                    }
                    _list = list;
                    _jsonElement = null;
                }
                return _list;
            }
        }

        internal override JsonValueKind GetValueKindCore() => JsonValueKind.Array;

        internal override JsonNode DeepCloneCore()
        {
            if (_list == null)
                return _jsonElement.HasValue ? Real(new JsonArrayImpl(_jsonElement.Value.Clone(), Options)) : Real(new JsonArrayImpl(Options));
            JsonArrayImpl clone = new JsonArrayImpl(Options);
            clone._list = new List<JsonNode>(_list.Count);
            foreach (JsonNode item in _list) clone.Add(item == null ? null : Impl(item).DeepCloneCore());
            return Real(clone);
        }

        internal override bool DeepEqualsCore(JsonNodeImpl node)
        {
            switch (node)
            {
                case JsonObjectImpl _:
                    return false;
                case JsonValueImpl value:
                    return value.DeepEqualsCore(this);
                case JsonArrayImpl other:
                    List<JsonNode> current = List;
                    List<JsonNode> otherList = other.List;
                    if (current.Count != otherList.Count) return false;
                    for (int i = 0; i < current.Count; i++)
                        if (!DeepEquals(current[i] == null ? null : Impl(current[i]), otherList[i] == null ? null : Impl(otherList[i]))) return false;
                    return true;
                default:
                    return false;
            }
        }

        public IEnumerable<T> GetValues<T>()
        {
            List<T> values = new List<T>();
            foreach (JsonNode item in List) values.Add(item == null ? (T)(object)null : Impl(item).GetValue<T>());
            return values;
        }

        public void Add<T>(T value) => Add(ConvertFromValue(value, Options));

        internal override JsonNode GetItem(int index) => List[index];

        internal override void SetItem(int index, JsonNode value)
        {
            if (value != null) Impl(value).AssignParent(Self);
            JsonNode old = List[index];
            if (old != null) Impl(old).Parent = null;
            List[index] = value;
        }

        internal override void AppendPath(StringBuilder path, JsonNode child)
        {
            if (Parent != null) Impl(Parent).AppendPath(path, Self);
            if (child != null) path.Append('[').Append(List.IndexOf(child)).Append(']');
        }

        public override void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (_list == null && _jsonElement.HasValue)
            {
                _jsonElement.Value.WriteTo(writer);
                return;
            }
            writer.WriteStartArray();
            foreach (JsonNode element in List)
            {
                if (element == null) writer.WriteNullValue();
                else Impl(element).WriteTo(writer, options);
            }
            writer.WriteEndArray();
        }

        public int Count => List.Count;

        public void Add(JsonNode item)
        {
            if (item != null) Impl(item).AssignParent(Self);
            List.Add(item);
        }

        public void Clear()
        {
            if (_list == null)
            {
                _jsonElement = null;
                _list = new List<JsonNode>();
                return;
            }
            foreach (JsonNode item in _list) if (item != null) Impl(item).Parent = null;
            _list.Clear();
        }

        public bool Contains(JsonNode item) => List.Contains(item);

        public int IndexOf(JsonNode item) => List.IndexOf(item);

        public void Insert(int index, JsonNode item)
        {
            if (item != null) Impl(item).AssignParent(Self);
            List.Insert(index, item);
        }

        public bool Remove(JsonNode item)
        {
            if (List.Remove(item))
            {
                if (item != null) Impl(item).Parent = null;
                return true;
            }
            return false;
        }

        public void RemoveAt(int index)
        {
            JsonNode item = List[index];
            List.RemoveAt(index);
            if (item != null) Impl(item).Parent = null;
        }

        public int RemoveAll(Func<JsonNode, bool> match)
        {
            if (match == null) throw new ArgumentNullException("match");
            return List.RemoveAll(node =>
            {
                if (match(node))
                {
                    if (node != null) Impl(node).Parent = null;
                    return true;
                }
                return false;
            });
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            List<JsonNode> list = List;
            if (list.Count - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            for (int i = 0; i < count; i++)
                if (list[index + i] != null) Impl(list[index + i]).Parent = null;
            list.RemoveRange(index, count);
        }

        JsonNode IList<JsonNode>.this[int index]
        {
            get => GetItem(index);
            set => SetItem(index, value);
        }

        void ICollection<JsonNode>.CopyTo(JsonNode[] array, int index) => List.CopyTo(array, index);

        public IEnumerator<JsonNode> GetEnumerator() => List.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => List.GetEnumerator();

        bool ICollection<JsonNode>.IsReadOnly => false;
    }

    [Shadows("System.Text.Json.Nodes.JsonValue")]
    public abstract class JsonValueImpl : JsonNodeImpl
    {
        private protected JsonValueImpl(JsonNodeOptions? options) : base(options) { }

        public bool TryGetValue<T>(out T value)
        {
            if (this is JsonValueOfElement element) return element.TryGetElementValue(out value);
            object boxed = UntypedValue;
            if (boxed is T typed)
            {
                value = typed;
                return true;
            }
            value = default(T);
            return false;
        }

        internal T GetValueCore<T>()
        {
            if (this is JsonValueOfElement element)
            {
                if (!element.TryGetElementValue(out T fromElement))
                    throw new InvalidOperationException("An element of type '" + JsonThrowHelper.ValueKindName(element.Value.ValueKind) + "' cannot be converted to a '" + typeof(T).ToString() + "'.");
                return fromElement;
            }
            object boxed = UntypedValue;
            if (boxed is T typed) return typed;
            throw new InvalidOperationException("A value of type '" + ValueType.ToString() + "' cannot be converted to a '" + typeof(T).ToString() + "'.");
        }

        internal abstract object UntypedValue { get; }

        internal abstract Type ValueType { get; }

        internal override void AppendPath(StringBuilder path, JsonNode child)
        {
            if (Parent != null) Impl(Parent).AppendPath(path, Self);
        }

        internal override bool DeepEqualsCore(JsonNodeImpl otherNode)
        {
            if (GetValueKind() != otherNode.GetValueKind()) return false;
            return JsonElementImpl.DeepEquals(ToElement(this), ToElement(otherNode));
        }

        private static JsonElementImpl ToElement(JsonNodeImpl node)
        {
            if (node.UnderlyingElement is JsonElementImpl element) return element;
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(new JsonWriterOptionsImpl());
            node.WriteTo(writer, null);
            return JsonDocumentImpl.ParseValue(writer.WrittenBytes(), default).RootElement;
        }

        public static JsonValueImpl Create<T>(T value, JsonNodeOptions? options = null)
        {
            if (value == null) return null;
            object boxed = value;
            if (boxed is JsonNode) throw new ArgumentException("A JsonNode cannot be used as a value.", "value");
            if (boxed is JsonElement element) return CreateFromElement((JsonElementImpl)(object)element, options);
            return CreateWithDefaultMetadata(value, options);
        }

        public static JsonValueImpl Create<T>(T value, JsonTypeInfoImpl<T> jsonTypeInfo, JsonNodeOptions? options = null)
        {
            if (jsonTypeInfo == null) throw new ArgumentNullException("jsonTypeInfo");
            if (value == null) return null;
            object boxed = value;
            if (boxed is JsonNode) throw new ArgumentException("A JsonNode cannot be used as a value.", "value");
            if (boxed is JsonElement element) return CreateFromElement((JsonElementImpl)(object)element, options);
            jsonTypeInfo.EnsureConfigured();
            return new JsonValueCustomized<T>(value, jsonTypeInfo, options);
        }

        internal static JsonValueImpl CreateWithDefaultMetadata<T>(T value, JsonNodeOptions? options)
        {
            JsonConverterImpl primitive = JsonBuiltInTypeInfos.DictionaryKeyConverter<T>(null);
            if (primitive != null && typeof(T) != typeof(object)) return new JsonValuePrimitive<T>(value, (JsonConverterImpl<T>)primitive, options);
            JsonTypeInfoImpl<T> info = (JsonTypeInfoImpl<T>)JsonSerializerOptionsImpl.Default.GetTypeInfo(typeof(T));
            info.EnsureConfigured();
            if (info.Kind == JsonTypeInfoKind.None && info._converter.IsInternalConverter)
                return new JsonValuePrimitive<T>(value, info.TypedConverter, options);
            return new JsonValueCustomized<T>(value, info, options);
        }

        internal static JsonValueImpl CreateFromElement(JsonElementImpl element, JsonNodeOptions? options)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    throw new InvalidOperationException("The element cannot be an object or array.");
                default: return new JsonValueOfElement(element, options);
            }
        }

        public static JsonValueImpl Create(bool value, JsonNodeOptions? options = null) => new JsonValuePrimitive<bool>(value, JsonMetadataServicesImpl.BooleanConverter, options);
        public static JsonValueImpl Create(bool? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(byte value, JsonNodeOptions? options = null) => new JsonValuePrimitive<byte>(value, JsonMetadataServicesImpl.ByteConverter, options);
        public static JsonValueImpl Create(byte? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(char value, JsonNodeOptions? options = null) => new JsonValuePrimitive<char>(value, JsonMetadataServicesImpl.CharConverter, options);
        public static JsonValueImpl Create(char? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(DateTime value, JsonNodeOptions? options = null) => new JsonValuePrimitive<DateTime>(value, JsonMetadataServicesImpl.DateTimeConverter, options);
        public static JsonValueImpl Create(DateTime? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(DateTimeOffset value, JsonNodeOptions? options = null) => new JsonValuePrimitive<DateTimeOffset>(value, JsonMetadataServicesImpl.DateTimeOffsetConverter, options);
        public static JsonValueImpl Create(DateTimeOffset? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(decimal value, JsonNodeOptions? options = null) => new JsonValuePrimitive<decimal>(value, JsonMetadataServicesImpl.DecimalConverter, options);
        public static JsonValueImpl Create(decimal? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(double value, JsonNodeOptions? options = null) => new JsonValuePrimitive<double>(value, JsonMetadataServicesImpl.DoubleConverter, options);
        public static JsonValueImpl Create(double? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(short value, JsonNodeOptions? options = null) => new JsonValuePrimitive<short>(value, JsonMetadataServicesImpl.Int16Converter, options);
        public static JsonValueImpl Create(short? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(int value, JsonNodeOptions? options = null) => new JsonValuePrimitive<int>(value, JsonMetadataServicesImpl.Int32Converter, options);
        public static JsonValueImpl Create(int? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(long value, JsonNodeOptions? options = null) => new JsonValuePrimitive<long>(value, JsonMetadataServicesImpl.Int64Converter, options);
        public static JsonValueImpl Create(long? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(sbyte value, JsonNodeOptions? options = null) => new JsonValuePrimitive<sbyte>(value, JsonMetadataServicesImpl.SByteConverter, options);
        public static JsonValueImpl Create(sbyte? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(float value, JsonNodeOptions? options = null) => new JsonValuePrimitive<float>(value, JsonMetadataServicesImpl.SingleConverter, options);
        public static JsonValueImpl Create(float? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(string value, JsonNodeOptions? options = null) => value == null ? null : new JsonValuePrimitive<string>(value, JsonMetadataServicesImpl.StringConverter, options);
        public static JsonValueImpl Create(ushort value, JsonNodeOptions? options = null) => new JsonValuePrimitive<ushort>(value, JsonMetadataServicesImpl.UInt16Converter, options);
        public static JsonValueImpl Create(ushort? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(uint value, JsonNodeOptions? options = null) => new JsonValuePrimitive<uint>(value, JsonMetadataServicesImpl.UInt32Converter, options);
        public static JsonValueImpl Create(uint? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(ulong value, JsonNodeOptions? options = null) => new JsonValuePrimitive<ulong>(value, JsonMetadataServicesImpl.UInt64Converter, options);
        public static JsonValueImpl Create(ulong? value, JsonNodeOptions? options = null) => value.HasValue ? Create(value.Value, options) : null;
        public static JsonValueImpl Create(JsonElementImpl value, JsonNodeOptions? options = null) => CreateFromElement(value, options);
        public static JsonValueImpl Create(JsonElementImpl? value, JsonNodeOptions? options = null) => value.HasValue ? CreateFromElement(value.Value, options) : null;
    }

    /// <summary>JsonValue&lt;TValue&gt;: a CLR value (Create, implicit conversions).</summary>
    internal abstract class JsonValueOfT<TValue> : JsonValueImpl
    {
        internal readonly TValue Value;

        protected JsonValueOfT(TValue value, JsonNodeOptions? options) : base(options)
        {
            Value = value;
        }

        internal override object UntypedValue => Value;

        internal override Type ValueType => typeof(TValue);
    }

    internal sealed class JsonValuePrimitive<TValue> : JsonValueOfT<TValue>
    {
        private readonly JsonConverterImpl<TValue> _converter;

        public JsonValuePrimitive(TValue value, JsonConverterImpl<TValue> converter, JsonNodeOptions? options) : base(value, options)
        {
            _converter = converter;
        }

        internal override JsonValueKind GetValueKindCore()
        {
            object boxed = Value;
            if (boxed is bool b) return b ? JsonValueKind.True : JsonValueKind.False;
            if (boxed is string || boxed is char || boxed is DateTime || boxed is DateTimeOffset || boxed is TimeSpan) return JsonValueKind.String;
            return JsonValueKind.Number;
        }

        internal override JsonNode DeepCloneCore() => Real(new JsonValuePrimitive<TValue>(Value, _converter, Options));

        internal override bool DeepEqualsCore(JsonNodeImpl otherNode)
        {
            if (otherNode is JsonValueImpl otherValue && otherValue.TryGetValue(out TValue v)) return CoreIntrinsics.AreEqual(Value, v);
            return base.DeepEqualsCore(otherNode);
        }

        public override void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            options ??= JsonValueDefaults.Options;
            if (_converter.IsNumberConverter) _converter.WriteNumberWithCustomHandling(writer, Value, options.NumberHandling, options);
            else _converter.Write(writer, Value, options);
        }
    }

    internal static class JsonValueDefaults
    {
        private static JsonSerializerOptionsImpl s_options;
        public static JsonSerializerOptionsImpl Options => s_options ??= new JsonSerializerOptionsImpl();
    }

    internal sealed class JsonValueCustomized<TValue> : JsonValueOfT<TValue>
    {
        private readonly JsonTypeInfoImpl<TValue> _typeInfo;

        public JsonValueCustomized(TValue value, JsonTypeInfoImpl<TValue> typeInfo, JsonNodeOptions? options) : base(value, options)
        {
            _typeInfo = typeInfo;
        }

        internal override JsonValueKind GetValueKindCore()
        {
            Utf8JsonWriterImpl writer = new Utf8JsonWriterImpl(new JsonWriterOptionsImpl());
            WriteTo(writer, null);
            Utf8JsonReaderImpl reader = new Utf8JsonReaderImpl(writer.WrittenBytes(), new JsonReaderOptionsImpl());
            reader.Read();
            return JsonReaderHelper.ToValueKind(reader.TokenType);
        }

        internal override JsonNode DeepCloneCore() => Real(JsonSerializerImpl.SerializeToNode(Value, _typeInfo));

        public override void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            JsonTypeInfoImpl info = _typeInfo;
            if (options != null && !ReferenceEquals(options, info._options))
            {
                options.ConfigureForJsonSerializer();
                info = options.GetTypeInfoForRuntimeType(typeof(TValue));
            }
            JsonSerializerImpl.WriteCore(writer, Value, info);
        }
    }

    /// <summary>A parsed primitive value, backed by its JsonElement.</summary>
    internal sealed class JsonValueOfElement : JsonValueOfT<JsonElementImpl>
    {
        public JsonValueOfElement(JsonElementImpl value, JsonNodeOptions? options) : base(value, options) { }

        internal override JsonElementImpl? UnderlyingElement => Value;

        internal override JsonNode DeepCloneCore() => Real(new JsonValueOfElement(Value.Clone(), Options));

        internal override JsonValueKind GetValueKindCore() => Value.ValueKind;

        internal override bool DeepEqualsCore(JsonNodeImpl otherNode)
        {
            if (otherNode.UnderlyingElement is JsonElementImpl otherElement) return JsonElementImpl.DeepEquals(Value, otherElement);
            if (otherNode is JsonValueImpl) return otherNode.DeepEqualsCore(this);
            return base.DeepEqualsCore(otherNode);
        }

        public override void WriteTo(Utf8JsonWriterImpl writer, JsonSerializerOptionsImpl options = null)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            Value.WriteTo(writer);
        }

        internal bool TryGetElementValue<T>(out T value)
        {
            bool success;
            if (typeof(T) == typeof(JsonElement) || typeof(T) == typeof(JsonElement?) || typeof(T) == typeof(object))
            {
                value = (T)(object)Value;
                return true;
            }
            switch (Value.ValueKind)
            {
                case JsonValueKind.Number:
                    if (typeof(T) == typeof(int) || typeof(T) == typeof(int?)) { success = Value.TryGetInt32(out int r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(long) || typeof(T) == typeof(long?)) { success = Value.TryGetInt64(out long r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(double) || typeof(T) == typeof(double?)) { success = Value.TryGetDouble(out double r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(short) || typeof(T) == typeof(short?)) { success = Value.TryGetInt16(out short r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(decimal) || typeof(T) == typeof(decimal?)) { success = Value.TryGetDecimal(out decimal r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(byte) || typeof(T) == typeof(byte?)) { success = Value.TryGetByte(out byte r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(float) || typeof(T) == typeof(float?)) { success = Value.TryGetSingle(out float r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(uint) || typeof(T) == typeof(uint?)) { success = Value.TryGetUInt32(out uint r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(ushort) || typeof(T) == typeof(ushort?)) { success = Value.TryGetUInt16(out ushort r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(ulong) || typeof(T) == typeof(ulong?)) { success = Value.TryGetUInt64(out ulong r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(sbyte) || typeof(T) == typeof(sbyte?)) { success = Value.TryGetSByte(out sbyte r); value = (T)(object)r; return success; }
                    break;
                case JsonValueKind.String:
                    if (typeof(T) == typeof(string))
                    {
                        value = (T)(object)Value.GetString();
                        return true;
                    }
                    if (typeof(T) == typeof(DateTime) || typeof(T) == typeof(DateTime?)) { success = Value.TryGetDateTime(out DateTime r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(DateTimeOffset) || typeof(T) == typeof(DateTimeOffset?)) { success = Value.TryGetDateTimeOffset(out DateTimeOffset r); value = (T)(object)r; return success; }
                    if (typeof(T) == typeof(char) || typeof(T) == typeof(char?))
                    {
                        string s = Value.GetString();
                        if (s.Length == 1)
                        {
                            value = (T)(object)s[0];
                            return true;
                        }
                    }
                    break;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    if (typeof(T) == typeof(bool) || typeof(T) == typeof(bool?))
                    {
                        value = (T)(object)Value.GetBoolean();
                        return true;
                    }
                    break;
            }
            value = default(T);
            return false;
        }
    }

    internal sealed class JsonNodeConverter : JsonConverterImpl<JsonNode>
    {
        internal override bool IsInternalConverter => true;

        public override bool HandleNull => true;

        public override JsonNode Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            return (JsonNode)JsonNodeImpl.ParseValue(ref reader, options);
        }

        public override void Write(Utf8JsonWriterImpl writer, JsonNode value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else JsonNodeImpl.Impl(value).WriteTo(writer, options);
        }
    }

    internal sealed class JsonNodeSubtypeConverter<TNode> : JsonConverterImpl<TNode> where TNode : class
    {
        internal override bool IsInternalConverter => true;

        public override bool HandleNull => true;

        public override TNode Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            object node = JsonNodeImpl.ParseValue(ref reader, options);
            if (node is TNode typed) return typed;
            if (typeof(TNode) == typeof(JsonObject)) throw JsonThrowHelper.GetInvalidOperationException_ExpectedObject(reader.TokenType);
            if (typeof(TNode) == typeof(JsonArray)) throw JsonThrowHelper.GetInvalidOperationException_ExpectedArray(reader.TokenType);
            throw JsonSerializerHelpers.DefaultJsonException();
        }

        public override void Write(Utf8JsonWriterImpl writer, TNode value, JsonSerializerOptionsImpl options)
        {
            if (value == null) writer.WriteNullValue();
            else ((JsonNodeImpl)(object)value).WriteTo(writer, options);
        }
    }
}
