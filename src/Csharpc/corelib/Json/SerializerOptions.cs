// System.Text.Json serializer configuration: JsonSerializerOptions, naming policies, JsonSerializerContext.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json (release/11.0): JsonSerializerOptions*.cs,
// Common/Json*NamingPolicy.cs, JsonSerializerContext.cs. Type metadata comes from source-generated
// contexts; where .NET falls back to reflection (DefaultJsonTypeInfoResolver), csharpc uses the contexts
// it synthesizes at compile time for the program's JsonSerializer calls (Frontend/JsonReflectionContexts.cs).
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CsharpcCoreLib
{
    /// <summary>
    /// Compile-time hooks: the synthesized contexts and the reflection switch, generated into the program
    /// as CsharpcCoreLib.ProgramHooks (Frontend/JsonReflectionContexts.cs) and called through core
    /// intrinsics, which work whether the corelib is compiled with the program or precompiled (ADR 0015).
    /// </summary>
    internal static class JsonReflection
    {
        /// <summary>DefaultJsonTypeInfoResolver's metadata for a built-in type (JsonBuiltInTypeInfos.TryCreate when linked in).</summary>
        internal static JsonTypeInfoImpl BuiltIn(Type type, JsonSerializerOptionsImpl options) =>
            (JsonTypeInfoImpl)CoreIntrinsics.JsonBuiltInHook(type, options);

        private static bool s_configured;
        private static bool s_enabled;
        private static IJsonTypeInfoResolver s_resolver;

        private static void EnsureConfigured()
        {
            if (s_configured) return;
            bool enabled = true;
            IJsonTypeInfoResolver resolver = null;
            CoreIntrinsics.JsonConfigureHook(ref enabled, ref resolver);
            s_enabled = enabled;
            s_resolver = new ReflectionStandInResolver(resolver);
            s_configured = true;
        }

        public static bool IsReflectionEnabledByDefault
        {
            get
            {
                EnsureConfigured();
                return s_enabled;
            }
        }

        /// <summary>The stand-in for DefaultJsonTypeInfoResolver: metadata generated at compile time.</summary>
        public static IJsonTypeInfoResolver Resolver
        {
            get
            {
                EnsureConfigured();
                return s_resolver ??= new ReflectionStandInResolver(null);
            }
        }

        private static IJsonTypeInfoResolver s_empty;

        /// <summary>JsonSerializerOptions.Default's resolver: the reflection stand-in, or EmptyJsonTypeInfoResolver when reflection is off.</summary>
        public static IJsonTypeInfoResolver DefaultResolver => IsReflectionEnabledByDefault ? Resolver : (s_empty ??= new EmptyJsonTypeInfoResolverImpl());

        /// <summary>Whether a resolver gives DefaultJsonTypeInfoResolver's answers (built-in types included).</summary>
        public static bool IsReflectionResolver(IJsonTypeInfoResolver resolver)
        {
            if (resolver == null) return false;
            if (resolver is ReflectionStandInResolver || resolver is DefaultJsonTypeInfoResolverImpl) return true;
            EnsureConfigured();
            if (ReferenceEquals(resolver, s_resolver)) return true;
            if (resolver is CombinedResolver combined)
                foreach (IJsonTypeInfoResolver r in combined.Resolvers)
                    if (IsReflectionResolver(r)) return true;
            return false;
        }
    }

    /// <summary>Synthesized contexts plus the built-in types: what DefaultJsonTypeInfoResolver resolves.</summary>
    internal sealed class ReflectionStandInResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver _contexts;

        public ReflectionStandInResolver(IJsonTypeInfoResolver contexts) { _contexts = contexts; }

        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            JsonTypeInfo info = _contexts?.GetTypeInfo(type, options);
            if (info != null) ((JsonTypeInfoImpl)(object)info).ReflectionSemantics = true;
            else info = (JsonTypeInfo)(object)JsonReflection.BuiltIn(type, (JsonSerializerOptionsImpl)(object)options);
            return info;
        }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.EmptyJsonTypeInfoResolver")]
    internal sealed class EmptyJsonTypeInfoResolverImpl : IJsonTypeInfoResolver
    {
        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options) => null;
    }

    [Shadows("System.Text.Json.JsonSerializerOptions")]
    public sealed class JsonSerializerOptionsImpl
    {
        internal const int DefaultMaxDepth = 64;

        private static JsonSerializerOptionsImpl s_default;
        private static JsonSerializerOptionsImpl s_web;
        private static JsonSerializerOptionsImpl s_strict;

        private IJsonTypeInfoResolver _typeInfoResolver;
        private JsonNamingPolicyImpl _dictionaryKeyPolicy;
        private JsonNamingPolicyImpl _jsonPropertyNamingPolicy;
        private JsonCommentHandling _readCommentHandling;
        private ReferenceHandlerImpl _referenceHandler;
        private JavaScriptEncoderImpl _encoder;
        private ConverterList _converters;
        private ResolverChain _resolverChain;
        private JsonIgnoreCondition _defaultIgnoreCondition;
        private JsonNumberHandling _numberHandling;
        private JsonObjectCreationHandling _preferredObjectCreationHandling;
        private JsonUnknownTypeHandling _unknownTypeHandling;
        private JsonUnmappedMemberHandling _unmappedMemberHandling;
        private int _defaultBufferSize = 16 * 1024;
        private int _maxDepth;
        private bool _allowOutOfOrderMetadataProperties;
        private bool _allowTrailingCommas;
        private bool _respectNullableAnnotations;
        private bool _respectRequiredConstructorParameters;
        private bool _ignoreNullValues;
        private bool _ignoreReadOnlyProperties;
        private bool _ignoreReadOnlyFields;
        private bool _includeFields;
        private bool _propertyNameCaseInsensitive;
        private bool _writeIndented;
        private bool _allowDuplicateProperties = true;
        private char _indentCharacter = ' ';
        private int _indentSize = 2;
        private string _newLine;
        private bool _isReadOnly;

        // Type metadata cache, per options instance (JsonSerializerOptions.Caching.cs).
        private Dictionary<Type, JsonTypeInfoImpl> _cache;

        public JsonSerializerOptionsImpl()
        {
            _newLine = Environment.NewLine;
        }

        public JsonSerializerOptionsImpl(JsonSerializerOptionsImpl options)
        {
            if (options == null) throw new ArgumentNullException("options");
            _dictionaryKeyPolicy = options._dictionaryKeyPolicy;
            _jsonPropertyNamingPolicy = options._jsonPropertyNamingPolicy;
            _readCommentHandling = options._readCommentHandling;
            _referenceHandler = options._referenceHandler;
            _encoder = options._encoder;
            _defaultIgnoreCondition = options._defaultIgnoreCondition;
            _numberHandling = options._numberHandling;
            _preferredObjectCreationHandling = options._preferredObjectCreationHandling;
            _unknownTypeHandling = options._unknownTypeHandling;
            _unmappedMemberHandling = options._unmappedMemberHandling;
            _defaultBufferSize = options._defaultBufferSize;
            _maxDepth = options._maxDepth;
            _allowOutOfOrderMetadataProperties = options._allowOutOfOrderMetadataProperties;
            _allowTrailingCommas = options._allowTrailingCommas;
            _respectNullableAnnotations = options._respectNullableAnnotations;
            _respectRequiredConstructorParameters = options._respectRequiredConstructorParameters;
            _ignoreNullValues = options._ignoreNullValues;
            _ignoreReadOnlyProperties = options._ignoreReadOnlyProperties;
            _ignoreReadOnlyFields = options._ignoreReadOnlyFields;
            _includeFields = options._includeFields;
            _propertyNameCaseInsensitive = options._propertyNameCaseInsensitive;
            _writeIndented = options._writeIndented;
            _allowDuplicateProperties = options._allowDuplicateProperties;
            _indentCharacter = options._indentCharacter;
            _indentSize = options._indentSize;
            _newLine = options._newLine;
            _typeInfoResolver = options._typeInfoResolver;
            if (options._converters != null)
            {
                _converters = new ConverterList(this);
                foreach (JsonConverter c in options._converters) _converters.Add(c);
            }
        }

        public JsonSerializerOptionsImpl(JsonSerializerDefaults defaults) : this()
        {
            if (defaults == JsonSerializerDefaults.Web)
            {
                _propertyNameCaseInsensitive = true;
                _jsonPropertyNamingPolicy = JsonNamingPolicyImpl.CamelCase;
                _numberHandling = JsonNumberHandling.AllowReadingFromString;
            }
            else if (defaults == JsonSerializerDefaults.Strict)
            {
                _unmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                _allowDuplicateProperties = false;
                _respectNullableAnnotations = true;
                _respectRequiredConstructorParameters = true;
            }
            else if (defaults != JsonSerializerDefaults.General)
            {
                throw new ArgumentOutOfRangeException("defaults");
            }
        }

        public static JsonSerializerOptionsImpl Default
        {
            get
            {
                if (s_default == null)
                {
                    JsonSerializerOptionsImpl options = new JsonSerializerOptionsImpl();
                    options._typeInfoResolver = JsonReflection.DefaultResolver;
                    options._isReadOnly = true;
                    s_default = options;
                }
                return s_default;
            }
        }

        public static JsonSerializerOptionsImpl Web
        {
            get
            {
                if (s_web == null)
                {
                    JsonSerializerOptionsImpl options = new JsonSerializerOptionsImpl(JsonSerializerDefaults.Web);
                    options._typeInfoResolver = JsonReflection.DefaultResolver;
                    options._isReadOnly = true;
                    s_web = options;
                }
                return s_web;
            }
        }

        public static JsonSerializerOptionsImpl Strict
        {
            get
            {
                if (s_strict == null)
                {
                    JsonSerializerOptionsImpl options = new JsonSerializerOptionsImpl(JsonSerializerDefaults.Strict);
                    options._typeInfoResolver = JsonReflection.DefaultResolver;
                    options._isReadOnly = true;
                    s_strict = options;
                }
                return s_strict;
            }
        }

        private void VerifyMutable()
        {
            if (_isReadOnly)
            {
                if (_typeInfoResolver is JsonSerializerContext)
                    throw new InvalidOperationException("JsonSerializerOptions instances cannot be modified once encapsulated by a JsonSerializerContext. Such encapsulation can happen either when calling 'JsonSerializerOptions.AddContext' or when passing the options instance to a JsonSerializerContext constructor.");
                throw new InvalidOperationException(JsonSR.SerializerOptionsReadOnly);
            }
        }

        public bool IsReadOnly => _isReadOnly;

        public void MakeReadOnly()
        {
            if (_typeInfoResolver == null)
                throw new InvalidOperationException("JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.");
            _isReadOnly = true;
        }

        public void MakeReadOnly(bool populateMissingResolver)
        {
            if (populateMissingResolver && _typeInfoResolver == null && JsonReflection.IsReflectionEnabledByDefault)
                _typeInfoResolver = JsonReflection.Resolver;
            MakeReadOnly();
        }

        public IJsonTypeInfoResolver TypeInfoResolver
        {
            get => _typeInfoResolver;
            set
            {
                VerifyMutable();
                _typeInfoResolver = value;
            }
        }

        public IList<IJsonTypeInfoResolver> TypeInfoResolverChain => _resolverChain ??= new ResolverChain(this);

        public bool AllowOutOfOrderMetadataProperties
        {
            get => _allowOutOfOrderMetadataProperties;
            set { VerifyMutable(); _allowOutOfOrderMetadataProperties = value; }
        }

        public bool AllowTrailingCommas
        {
            get => _allowTrailingCommas;
            set { VerifyMutable(); _allowTrailingCommas = value; }
        }

        public bool AllowDuplicateProperties
        {
            get => _allowDuplicateProperties;
            set { VerifyMutable(); _allowDuplicateProperties = value; }
        }

        public int DefaultBufferSize
        {
            get => _defaultBufferSize;
            set
            {
                VerifyMutable();
                if (value < 1) throw new ArgumentException("The value must be greater than zero.");
                _defaultBufferSize = value;
            }
        }

        public JavaScriptEncoderImpl Encoder
        {
            get => _encoder;
            set { VerifyMutable(); _encoder = value; }
        }

        public JsonNamingPolicyImpl DictionaryKeyPolicy
        {
            get => _dictionaryKeyPolicy;
            set { VerifyMutable(); _dictionaryKeyPolicy = value; }
        }

        public bool IgnoreNullValues
        {
            get => _ignoreNullValues;
            set
            {
                VerifyMutable();
                if (value && _defaultIgnoreCondition != JsonIgnoreCondition.Never)
                    throw new InvalidOperationException("'IgnoreNullValues' and 'DefaultIgnoreCondition' cannot both be set to non-default values.");
                _ignoreNullValues = value;
            }
        }

        public JsonIgnoreCondition DefaultIgnoreCondition
        {
            get => _defaultIgnoreCondition;
            set
            {
                VerifyMutable();
                if (value == JsonIgnoreCondition.Always)
                    throw new ArgumentException("The value cannot be 'JsonIgnoreCondition.Always'.");
                if (value != JsonIgnoreCondition.Never && _ignoreNullValues)
                    throw new InvalidOperationException("'IgnoreNullValues' and 'DefaultIgnoreCondition' cannot both be set to non-default values.");
                _defaultIgnoreCondition = value;
            }
        }

        public JsonNumberHandling NumberHandling
        {
            get => _numberHandling;
            set
            {
                VerifyMutable();
                if (!JsonSerializerHelpers.IsValidNumberHandlingValue(value)) throw new ArgumentOutOfRangeException("value");
                _numberHandling = value;
            }
        }

        public JsonObjectCreationHandling PreferredObjectCreationHandling
        {
            get => _preferredObjectCreationHandling;
            set
            {
                VerifyMutable();
                if ((uint)value > 1) throw new ArgumentOutOfRangeException("value");
                _preferredObjectCreationHandling = value;
            }
        }

        public bool IgnoreReadOnlyProperties
        {
            get => _ignoreReadOnlyProperties;
            set { VerifyMutable(); _ignoreReadOnlyProperties = value; }
        }

        public bool IgnoreReadOnlyFields
        {
            get => _ignoreReadOnlyFields;
            set { VerifyMutable(); _ignoreReadOnlyFields = value; }
        }

        public bool IncludeFields
        {
            get => _includeFields;
            set { VerifyMutable(); _includeFields = value; }
        }

        public int MaxDepth
        {
            get => _maxDepth;
            set
            {
                VerifyMutable();
                if (value < 0) throw new ArgumentOutOfRangeException("value", JsonSR.MaxDepthMustBePositive);
                _maxDepth = value;
            }
        }

        internal int EffectiveMaxDepth => _maxDepth == 0 ? DefaultMaxDepth : _maxDepth;

        public JsonNamingPolicyImpl PropertyNamingPolicy
        {
            get => _jsonPropertyNamingPolicy;
            set { VerifyMutable(); _jsonPropertyNamingPolicy = value; }
        }

        public bool PropertyNameCaseInsensitive
        {
            get => _propertyNameCaseInsensitive;
            set { VerifyMutable(); _propertyNameCaseInsensitive = value; }
        }

        public JsonCommentHandling ReadCommentHandling
        {
            get => _readCommentHandling;
            set
            {
                VerifyMutable();
                if ((int)value > (int)JsonCommentHandling.Skip)
                    throw new ArgumentOutOfRangeException("value", "Comments cannot be stored when deserializing objects, only the Skip and Disallow comment handling modes are supported.");
                _readCommentHandling = value;
            }
        }

        public JsonUnknownTypeHandling UnknownTypeHandling
        {
            get => _unknownTypeHandling;
            set { VerifyMutable(); _unknownTypeHandling = value; }
        }

        public JsonUnmappedMemberHandling UnmappedMemberHandling
        {
            get => _unmappedMemberHandling;
            set { VerifyMutable(); _unmappedMemberHandling = value; }
        }

        public bool WriteIndented
        {
            get => _writeIndented;
            set { VerifyMutable(); _writeIndented = value; }
        }

        public char IndentCharacter
        {
            get => _indentCharacter;
            set
            {
                if (value != ' ' && value != '\t') throw new ArgumentOutOfRangeException("value", JsonSR.InvalidIndentCharacter);
                VerifyMutable();
                _indentCharacter = value;
            }
        }

        public int IndentSize
        {
            get => _indentSize;
            set
            {
                if (value < 0 || value > 127) throw new ArgumentOutOfRangeException("value", JsonSR.Format(JsonSR.InvalidIndentSize, 0, 127));
                VerifyMutable();
                _indentSize = value;
            }
        }

        public string NewLine
        {
            get => _newLine;
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                if (value != "\n" && value != "\r\n") throw new ArgumentOutOfRangeException("value", JsonSR.InvalidNewLine);
                VerifyMutable();
                _newLine = value;
            }
        }

        public ReferenceHandlerImpl ReferenceHandler
        {
            get => _referenceHandler;
            set { VerifyMutable(); _referenceHandler = value; }
        }

        public bool RespectNullableAnnotations
        {
            get => _respectNullableAnnotations;
            set { VerifyMutable(); _respectNullableAnnotations = value; }
        }

        public bool RespectRequiredConstructorParameters
        {
            get => _respectRequiredConstructorParameters;
            set { VerifyMutable(); _respectRequiredConstructorParameters = value; }
        }

        public IList<JsonConverter> Converters => _converters ??= new ConverterList(this);

        internal bool HasConverters => _converters != null && _converters.Count > 0;

        internal JsonWriterOptionsImpl GetWriterOptions()
        {
            JsonWriterOptionsImpl options = new JsonWriterOptionsImpl();
            options.Encoder = _encoder;
            options.Indented = _writeIndented;
            options.IndentCharacter = _indentCharacter;
            options.IndentSize = _indentSize;
            options.MaxDepth = EffectiveMaxDepth;
            options.NewLine = _newLine;
            options.SkipValidation = true;
            return options;
        }

        internal JsonReaderOptionsImpl GetReaderOptions()
        {
            JsonReaderOptionsImpl options = new JsonReaderOptionsImpl();
            options.AllowTrailingCommas = _allowTrailingCommas;
            options.CommentHandling = _readCommentHandling;
            options.MaxDepth = EffectiveMaxDepth;
            return options;
        }

        internal JsonDocumentOptionsImpl GetDocumentOptions()
        {
            JsonDocumentOptionsImpl options = new JsonDocumentOptionsImpl();
            options.AllowTrailingCommas = _allowTrailingCommas;
            options.CommentHandling = _readCommentHandling;
            options.MaxDepth = _maxDepth;
            return options;
        }

        // ------------------------------------------------------------------ type metadata

        /// <summary>JsonSerializerOptions.ConfigureForJsonSerializer: the default resolver when none was set.</summary>
        internal void ConfigureForJsonSerializer()
        {
            if (JsonReflection.IsReflectionEnabledByDefault)
            {
                if (_typeInfoResolver == null) _typeInfoResolver = JsonReflection.Resolver;
            }
            else if (_typeInfoResolver == null || _typeInfoResolver is EmptyJsonTypeInfoResolverImpl)
            {
                throw new InvalidOperationException("Reflection-based serialization has been disabled for this application. Either use the source generator APIs or explicitly configure the 'JsonSerializerOptions.TypeInfoResolver' property.");
            }
            _isReadOnly = true;
        }

        public JsonTypeInfoImpl GetTypeInfo(Type type)
        {
            if (type == null) throw new ArgumentNullException("type");
            if (type == typeof(void)) throw new ArgumentException("The type 'System.Void' is not a valid type for serialization.", "type");
            if (_typeInfoResolver == null)
                throw new InvalidOperationException("JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.");
            _isReadOnly = true;
            JsonTypeInfoImpl info = GetTypeInfoInternal(type);
            if (info == null) ThrowNoMetadata(type);
            return info;
        }

        public JsonTypeInfoImpl<T> GetTypeInfo<T>() => (JsonTypeInfoImpl<T>)GetTypeInfo(typeof(T));

        public bool TryGetTypeInfo(Type type, out JsonTypeInfoImpl typeInfo)
        {
            if (type == null) throw new ArgumentNullException("type");
            if (_typeInfoResolver == null)
                throw new InvalidOperationException("JsonSerializerOptions instance must specify a TypeInfoResolver setting before being marked as read-only.");
            _isReadOnly = true;
            typeInfo = GetTypeInfoInternal(type);
            return typeInfo != null;
        }

        public bool TryGetTypeInfo<T>(out JsonTypeInfoImpl<T> typeInfo)
        {
            bool found = TryGetTypeInfo(typeof(T), out JsonTypeInfoImpl info);
            typeInfo = (JsonTypeInfoImpl<T>)info;
            return found;
        }

        internal void ThrowNoMetadata(Type type) => throw new NotSupportedException(NoMetadataMessage(type, _typeInfoResolver));

        internal static string NoMetadataMessage(Type type, object resolver) =>
            "JsonTypeInfo metadata for type '" + type.ToString() + "' was not provided by TypeInfoResolver of type '" + (resolver == null ? "<null>" : resolver.GetType().ToString())
                + "'. If using source generation, ensure that all root types passed to the serializer have been annotated with 'JsonSerializableAttribute', along with any types that might be serialized polymorphically.";

        /// <summary>Cached, configured metadata; null when no resolver knows the type.</summary>
        internal JsonTypeInfoImpl GetTypeInfoInternal(Type type)
        {
            _cache ??= new Dictionary<Type, JsonTypeInfoImpl>();
            if (_cache.TryGetValue(type, out JsonTypeInfoImpl cached)) return cached;
            JsonTypeInfoImpl info = _typeInfoResolver == null ? null : (JsonTypeInfoImpl)(object)_typeInfoResolver.GetTypeInfo(type, (JsonSerializerOptions)(object)this);
            // DefaultJsonTypeInfoResolver knows every built-in type; source-generated contexts only what they generated.
            if (info == null && JsonReflection.IsReflectionResolver(_typeInfoResolver)) info = JsonReflection.BuiltIn(type, this);
            if (info != null)
            {
                if (info.Type != type)
                    throw new InvalidOperationException("The IJsonTypeInfoResolver returned an incompatible JsonTypeInfo instance of type '" + info.Type.ToString() + "', expected type '" + type.ToString() + "'.");
                if (!ReferenceEquals(info.Options, this))
                    throw new InvalidOperationException("The IJsonTypeInfoResolver returned a JsonTypeInfo instance whose JsonSerializerOptions setting does not match the provided argument.");
            }
            _cache[type] = info;
            return info;
        }

        /// <summary>Metadata for serializing a value whose declared type is object (polymorphic by run-time type).</summary>
        internal JsonTypeInfoImpl GetTypeInfoForRuntimeType(Type type)
        {
            JsonTypeInfoImpl info = GetTypeInfoInternal(type);
            if (info == null) ThrowNoMetadata(type);
            info.EnsureConfigured();
            return info;
        }

        public JsonConverterImpl GetConverter(Type typeToConvert)
        {
            if (typeToConvert == null) throw new ArgumentNullException("typeToConvert");
            if (_converters != null)
            {
                foreach (JsonConverter converter in _converters)
                    if (converter.CanConvert(typeToConvert)) return JsonSerializerHelpers.ExpandConverter(typeToConvert, (JsonConverterImpl)(object)converter, this);
            }
            ConfigureForJsonSerializer();
            return GetTypeInfo(typeToConvert).Converter;
        }

        public void AddContext<TContext>() where TContext : JsonSerializerContext, new()
        {
            VerifyMutable();
            TContext context = new TContext();
            _typeInfoResolver = context;
        }

        internal sealed class ConverterList : IList<JsonConverter>
        {
            private readonly List<JsonConverter> _list = new List<JsonConverter>();
            private readonly JsonSerializerOptionsImpl _options;

            public ConverterList(JsonSerializerOptionsImpl options) { _options = options; }

            public JsonConverter this[int index]
            {
                get => _list[index];
                set
                {
                    if (value == null) throw new ArgumentNullException("value");
                    _options.VerifyMutable();
                    _list[index] = value;
                }
            }

            public int Count => _list.Count;
            public bool IsReadOnly => _options._isReadOnly;

            public void Add(JsonConverter item)
            {
                if (item == null) throw new ArgumentNullException("item");
                _options.VerifyMutable();
                _list.Add(item);
            }

            public void Clear() { _options.VerifyMutable(); _list.Clear(); }
            public bool Contains(JsonConverter item) => _list.Contains(item);
            public void CopyTo(JsonConverter[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
            public IEnumerator<JsonConverter> GetEnumerator() => _list.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
            public int IndexOf(JsonConverter item) => _list.IndexOf(item);

            public void Insert(int index, JsonConverter item)
            {
                if (item == null) throw new ArgumentNullException("item");
                _options.VerifyMutable();
                _list.Insert(index, item);
            }

            public bool Remove(JsonConverter item) { _options.VerifyMutable(); return _list.Remove(item); }
            public void RemoveAt(int index) { _options.VerifyMutable(); _list.RemoveAt(index); }
        }

        /// <summary>TypeInfoResolverChain: a view that keeps TypeInfoResolver in sync (a combined resolver).</summary>
        internal sealed class ResolverChain : IList<IJsonTypeInfoResolver>
        {
            private readonly JsonSerializerOptionsImpl _options;
            private readonly List<IJsonTypeInfoResolver> _list = new List<IJsonTypeInfoResolver>();

            public ResolverChain(JsonSerializerOptionsImpl options)
            {
                _options = options;
                if (options._typeInfoResolver is CombinedResolver combined) _list.AddRange(combined.Resolvers);
                else if (options._typeInfoResolver != null) _list.Add(options._typeInfoResolver);
            }

            private void Sync()
            {
                _options._typeInfoResolver = _list.Count == 0 ? null : _list.Count == 1 ? _list[0] : new CombinedResolver(_list.ToArray());
            }

            public IJsonTypeInfoResolver this[int index]
            {
                get => _list[index];
                set { _options.VerifyMutable(); _list[index] = value; Sync(); }
            }

            public int Count => _list.Count;
            public bool IsReadOnly => _options._isReadOnly;
            public void Add(IJsonTypeInfoResolver item) { _options.VerifyMutable(); _list.Add(item); Sync(); }
            public void Clear() { _options.VerifyMutable(); _list.Clear(); Sync(); }
            public bool Contains(IJsonTypeInfoResolver item) => _list.Contains(item);
            public void CopyTo(IJsonTypeInfoResolver[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);
            public IEnumerator<IJsonTypeInfoResolver> GetEnumerator() => _list.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
            public int IndexOf(IJsonTypeInfoResolver item) => _list.IndexOf(item);
            public void Insert(int index, IJsonTypeInfoResolver item) { _options.VerifyMutable(); _list.Insert(index, item); Sync(); }
            public bool Remove(IJsonTypeInfoResolver item) { _options.VerifyMutable(); bool r = _list.Remove(item); Sync(); return r; }
            public void RemoveAt(int index) { _options.VerifyMutable(); _list.RemoveAt(index); Sync(); }
        }
    }

    internal sealed class CombinedResolver : IJsonTypeInfoResolver
    {
        internal readonly IJsonTypeInfoResolver[] Resolvers;

        public CombinedResolver(IJsonTypeInfoResolver[] resolvers) { Resolvers = resolvers; }

        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            for (int i = 0; i < Resolvers.Length; i++)
            {
                JsonTypeInfo info = Resolvers[i].GetTypeInfo(type, options);
                if (info != null) return info;
            }
            return null;
        }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver")]
    public static class JsonTypeInfoResolverImpl
    {
        public static IJsonTypeInfoResolver Combine(params IJsonTypeInfoResolver[] resolvers)
        {
            if (resolvers == null) throw new ArgumentNullException("resolvers");
            List<IJsonTypeInfoResolver> list = new List<IJsonTypeInfoResolver>();
            foreach (IJsonTypeInfoResolver r in resolvers)
            {
                if (r == null) continue;
                if (r is CombinedResolver c) list.AddRange(c.Resolvers);
                else list.Add(r);
            }
            return list.Count == 1 ? list[0] : new CombinedResolver(list.ToArray());
        }

        public static IJsonTypeInfoResolver Combine(ReadOnlySpan<IJsonTypeInfoResolver> resolvers) => Combine(resolvers.ToArray());

        public static IJsonTypeInfoResolver WithAddedModifier(IJsonTypeInfoResolver resolver, Action<JsonTypeInfo> modifier)
        {
            if (resolver == null) throw new ArgumentNullException("resolver");
            if (modifier == null) throw new ArgumentNullException("modifier");
            return new ModifiedResolver(resolver, modifier);
        }

        private sealed class ModifiedResolver : IJsonTypeInfoResolver
        {
            private readonly IJsonTypeInfoResolver _source;
            private readonly Action<JsonTypeInfo> _modifier;

            public ModifiedResolver(IJsonTypeInfoResolver source, Action<JsonTypeInfo> modifier)
            {
                _source = source;
                _modifier = modifier;
            }

            public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
            {
                JsonTypeInfo info = _source.GetTypeInfo(type, options);
                if (info != null) _modifier(info);
                return info;
            }
        }
    }

    /// <summary>
    /// new DefaultJsonTypeInfoResolver(): .NET's reflection resolver; here the metadata csharpc generated at
    /// compile time for the program's serializer calls, with the user's modifiers applied.
    /// </summary>
    [Shadows("System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver")]
    public class DefaultJsonTypeInfoResolverImpl : IJsonTypeInfoResolver
    {
        private List<Action<JsonTypeInfo>> _modifiers;

        public DefaultJsonTypeInfoResolverImpl() { }

        public IList<Action<JsonTypeInfo>> Modifiers => _modifiers ??= new List<Action<JsonTypeInfo>>();

        public virtual JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (type == null) throw new ArgumentNullException("type");
            if (options == null) throw new ArgumentNullException("options");
            JsonSerializerOptionsImpl impl = (JsonSerializerOptionsImpl)(object)options;
            JsonTypeInfo info = JsonReflection.Resolver.GetTypeInfo(type, options);
            if (info == null) impl.ThrowNoMetadata(type);
            if (_modifiers != null)
                foreach (Action<JsonTypeInfo> modifier in _modifiers) modifier(info);
            return info;
        }
    }

    [Shadows("System.Text.Json.Serialization.JsonSerializerContext")]
    public abstract class JsonSerializerContextImpl : IJsonTypeInfoResolver
    {
        private JsonSerializerOptionsImpl _options;

        protected JsonSerializerContextImpl(JsonSerializerOptionsImpl options)
        {
            if (options != null)
            {
                options.TypeInfoResolver = this;
                options.MakeReadOnly();
                _options = options;
            }
        }

        public JsonSerializerOptionsImpl Options
        {
            get
            {
                JsonSerializerOptionsImpl options = _options;
                if (options == null)
                {
                    options = new JsonSerializerOptionsImpl();
                    options.TypeInfoResolver = this;
                    options.MakeReadOnly();
                    _options = options;
                }
                return options;
            }
        }

        protected abstract JsonSerializerOptionsImpl GeneratedSerializerOptions { get; }

        /// <summary>IBuiltInJsonTypeInfoResolver.IsCompatibleWithOptions: options the generated fast path honors.</summary>
        internal bool IsCompatibleWithOptions(JsonSerializerOptionsImpl options)
        {
            JsonSerializerOptionsImpl generated = GeneratedSerializerOptions;
            return generated != null
                && options.Converters.Count == 0
                && options.Encoder == null
                && (options.NumberHandling & (JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowNamedFloatingPointLiterals)) == 0
                && options.ReferenceHandler == null
#pragma warning disable SYSLIB0020
                && !options.IgnoreNullValues
#pragma warning restore SYSLIB0020
                && options.DefaultIgnoreCondition == generated.DefaultIgnoreCondition
                && options.RespectNullableAnnotations == generated.RespectNullableAnnotations
                && options.IgnoreReadOnlyFields == generated.IgnoreReadOnlyFields
                && options.IgnoreReadOnlyProperties == generated.IgnoreReadOnlyProperties
                && options.IncludeFields == generated.IncludeFields
                && options.PropertyNamingPolicy == generated.PropertyNamingPolicy
                && options.DictionaryKeyPolicy == null;
        }

        internal static bool IsCompatibleWithOptions(IJsonTypeInfoResolver resolver, JsonSerializerOptionsImpl options)
        {
            if (resolver is JsonSerializerContext context) return ((JsonSerializerContextImpl)(object)context).IsCompatibleWithOptions(options);
            if (resolver is CombinedResolver combined)
            {
                foreach (IJsonTypeInfoResolver r in combined.Resolvers)
                    if (!IsCompatibleWithOptions(r, options)) return false;
                return true;
            }
            return false;
        }

        public abstract JsonTypeInfoImpl GetTypeInfo(Type type);

        JsonTypeInfo IJsonTypeInfoResolver.GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (options != null && !ReferenceEquals(options, _options))
                throw new InvalidOperationException("Cannot resolve metadata from a JsonSerializerContext for options other than its own when called through its IJsonTypeInfoResolver interface.");
            return (JsonTypeInfo)(object)GetTypeInfo(type);
        }
    }

    [Shadows("System.Text.Json.JsonNamingPolicy")]
    public abstract class JsonNamingPolicyImpl
    {
        private static JsonNamingPolicyImpl s_camel;
        private static JsonNamingPolicyImpl s_snakeLower;
        private static JsonNamingPolicyImpl s_snakeUpper;
        private static JsonNamingPolicyImpl s_kebabLower;
        private static JsonNamingPolicyImpl s_kebabUpper;
        private static JsonNamingPolicyImpl s_pascal;

        protected JsonNamingPolicyImpl() { }

        public static JsonNamingPolicyImpl CamelCase => s_camel ??= new JsonCamelCaseNamingPolicy();
        public static JsonNamingPolicyImpl SnakeCaseLower => s_snakeLower ??= new JsonSeparatorNamingPolicy(JsonSeparatorNamingPolicy.WordCasing.LowerCase, '_');
        public static JsonNamingPolicyImpl SnakeCaseUpper => s_snakeUpper ??= new JsonSeparatorNamingPolicy(JsonSeparatorNamingPolicy.WordCasing.UpperCase, '_');
        public static JsonNamingPolicyImpl KebabCaseLower => s_kebabLower ??= new JsonSeparatorNamingPolicy(JsonSeparatorNamingPolicy.WordCasing.LowerCase, '-');
        public static JsonNamingPolicyImpl KebabCaseUpper => s_kebabUpper ??= new JsonSeparatorNamingPolicy(JsonSeparatorNamingPolicy.WordCasing.UpperCase, '-');
        public static JsonNamingPolicyImpl PascalCase => s_pascal ??= new JsonSeparatorNamingPolicy(JsonSeparatorNamingPolicy.WordCasing.PascalCase, '\0');

        public abstract string ConvertName(string name);
    }

    internal sealed class JsonCamelCaseNamingPolicy : JsonNamingPolicyImpl
    {
        public override string ConvertName(string name)
        {
            if (string.IsNullOrEmpty(name) || !char.IsUpper(name[0])) return name;
            char[] chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (i == 1 && !char.IsUpper(chars[i])) break;
                bool hasNext = i + 1 < chars.Length;
                // Stop when next char is already lowercase.
                if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                {
                    // If the next char is a space, lowercase current char before exiting.
                    if (chars[i + 1] == ' ') chars[i] = char.ToLowerInvariant(chars[i]);
                    break;
                }
                chars[i] = char.ToLowerInvariant(chars[i]);
            }
            return new string(chars);
        }
    }

    internal sealed class JsonSeparatorNamingPolicy : JsonNamingPolicyImpl
    {
        internal enum WordCasing { LowerCase, UpperCase, PascalCase }

        private const int NotStarted = 0, UppercaseLetter = 1, LowercaseLetterOrDigit = 2, SpaceSeparator = 3;

        private readonly char _separator; // '\0': none (PascalCase)
        private readonly WordCasing _wordCasing;

        internal JsonSeparatorNamingPolicy(WordCasing wordCasing, char separator)
        {
            _wordCasing = wordCasing;
            _separator = separator;
        }

        public override string ConvertName(string name)
        {
            if (name == null) throw new ArgumentNullException("name");
            StringBuilder destination = new StringBuilder(name.Length + name.Length / 5);
            bool hasSeparator = _separator != '\0';
            int state = NotStarted;
            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];
                UnicodeCategory category = char.GetUnicodeCategory(current);
                switch (category)
                {
                    case UnicodeCategory.UppercaseLetter:
                    {
                        bool isWordBoundary = false;
                        switch (state)
                        {
                            case NotStarted:
                                isWordBoundary = true;
                                break;
                            case LowercaseLetterOrDigit:
                            case SpaceSeparator:
                                isWordBoundary = true;
                                if (hasSeparator) destination.Append(_separator);
                                break;
                            case UppercaseLetter:
                                // 'XMLReader' -> 'xml_reader', 'SHA512Hash' -> 'sha512-hash'.
                                if (i + 1 < name.Length && char.IsLower(name[i + 1]))
                                {
                                    isWordBoundary = true;
                                    if (hasSeparator) destination.Append(_separator);
                                }
                                break;
                        }
                        if (_wordCasing == WordCasing.LowerCase) current = char.ToLowerInvariant(current);
                        else if (_wordCasing == WordCasing.PascalCase && !isWordBoundary) current = char.ToLowerInvariant(current);
                        destination.Append(current);
                        state = UppercaseLetter;
                        break;
                    }
                    case UnicodeCategory.LowercaseLetter:
                    case UnicodeCategory.DecimalDigitNumber:
                    {
                        bool isWordStart = state == SpaceSeparator || state == NotStarted;
                        if (state == SpaceSeparator && hasSeparator) destination.Append(_separator);
                        if (category == UnicodeCategory.LowercaseLetter)
                        {
                            if (_wordCasing == WordCasing.UpperCase) current = char.ToUpperInvariant(current);
                            else if (_wordCasing == WordCasing.PascalCase && isWordStart) current = char.ToUpperInvariant(current);
                        }
                        destination.Append(current);
                        state = LowercaseLetterOrDigit;
                        break;
                    }
                    case UnicodeCategory.SpaceSeparator:
                        if (state != NotStarted) state = SpaceSeparator;
                        break;
                    default:
                        destination.Append(current);
                        state = NotStarted;
                        break;
                }
            }
            return destination.ToString();
        }
    }

    [Shadows("System.Text.Json.Serialization.ReferenceHandler")]
    public abstract class ReferenceHandlerImpl
    {
        private static ReferenceHandlerImpl s_preserve;
        private static ReferenceHandlerImpl s_ignoreCycles;

        protected ReferenceHandlerImpl() { }

        internal int HandlingStrategy; // 1 = Preserve, 2 = IgnoreCycles

        public static ReferenceHandlerImpl Preserve => s_preserve ??= new BuiltInReferenceHandler(1);

        public static ReferenceHandlerImpl IgnoreCycles => s_ignoreCycles ??= new BuiltInReferenceHandler(2);

        public abstract ReferenceResolverImpl CreateResolver();

        internal virtual ReferenceResolverImpl CreateResolver(bool writing) => CreateResolver();
    }

    internal sealed class BuiltInReferenceHandler : ReferenceHandlerImpl
    {
        public BuiltInReferenceHandler(int strategy) { HandlingStrategy = strategy; }

        public override ReferenceResolverImpl CreateResolver() => new PreserveReferenceResolver();
    }

    [Shadows("System.Text.Json.Serialization.ReferenceResolver")]
    public abstract class ReferenceResolverImpl
    {
        protected ReferenceResolverImpl() { }
        public abstract void AddReference(string referenceId, object value);
        public abstract string GetReference(object value, out bool alreadyExists);
        public abstract object ResolveReference(string referenceId);
    }

    internal sealed class PreserveReferenceResolver : ReferenceResolverImpl
    {
        private Dictionary<string, object> _referenceIdToObjectMap;
        private List<object> _objects;
        private List<string> _ids;
        private uint _referenceCount;

        public override void AddReference(string referenceId, object value)
        {
            _referenceIdToObjectMap ??= new Dictionary<string, object>();
            if (!_referenceIdToObjectMap.TryAdd(referenceId, value))
                throw new JsonExceptionImpl("The value of the '$id' metadata property '" + referenceId + "' conflicts with an existing identifier.");
        }

        public override string GetReference(object value, out bool alreadyExists)
        {
            _objects ??= new List<object>();
            _ids ??= new List<string>();
            for (int i = 0; i < _objects.Count; i++)
            {
                if (ReferenceEquals(_objects[i], value))
                {
                    alreadyExists = true;
                    return _ids[i];
                }
            }
            _referenceCount++;
            string id = _referenceCount.ToString();
            _objects.Add(value);
            _ids.Add(id);
            alreadyExists = false;
            return id;
        }

        public override object ResolveReference(string referenceId)
        {
            if (_referenceIdToObjectMap == null || !_referenceIdToObjectMap.TryGetValue(referenceId, out object value))
                throw new JsonExceptionImpl("Reference '" + referenceId + "' was not found.") { AppendPathInformation = true };
            return value;
        }
    }
}
