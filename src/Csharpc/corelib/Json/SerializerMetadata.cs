// System.Text.Json.Serialization.Metadata: JsonTypeInfo, JsonPropertyInfo, the *InfoValues types the JSON
// source generator fills in, and JsonMetadataServices (the API generated code calls).
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Serialization/Metadata
// (release/11.0): property naming, ignore conditions, conflict resolution, ordering and the
// constructor-parameter matching follow JsonTypeInfo/JsonPropertyInfo's configuration logic.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CsharpcCoreLib
{
    [Shadows("System.Text.Json.Serialization.Metadata.JsonPropertyInfoValues`1")]
    public sealed class JsonPropertyInfoValuesImpl<T>
    {
        public JsonPropertyInfoValuesImpl() { }
        public bool IsProperty { get; set; }
        public bool IsPublic { get; set; }
        public bool IsVirtual { get; set; }
        public Type DeclaringType { get; set; }
        public JsonTypeInfo PropertyTypeInfo { get; set; }
        public JsonConverter<T> Converter { get; set; }
        public Func<object, T> Getter { get; set; }
        public Action<object, T> Setter { get; set; }
        public JsonIgnoreCondition? IgnoreCondition { get; set; }
        public bool HasJsonInclude { get; set; }
        public bool IsExtensionData { get; set; }
        public JsonNumberHandling? NumberHandling { get; set; }
        public string PropertyName { get; set; }
        public string JsonPropertyName { get; set; }
        public Func<ICustomAttributeProvider> AttributeProviderFactory { get; set; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonObjectInfoValues`1")]
    public sealed class JsonObjectInfoValuesImpl<T>
    {
        public JsonObjectInfoValuesImpl() { }
        public Func<T> ObjectCreator { get; set; }
        public Func<object[], T> ObjectWithParameterizedConstructorCreator { get; set; }
        public Func<JsonSerializerContext, JsonPropertyInfo[]> PropertyMetadataInitializer { get; set; }
        public Func<JsonParameterInfoValues[]> ConstructorParameterMetadataInitializer { get; set; }
        public Func<ICustomAttributeProvider> ConstructorAttributeProviderFactory { get; set; }
        public JsonNumberHandling NumberHandling { get; set; }
        public Action<Utf8JsonWriter, T> SerializeHandler { get; set; }
        public JsonPolymorphismOptions PolymorphismOptions { get; set; }
        public JsonTypeClassifierFactory TypeClassifierFactory { get; set; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonCollectionInfoValues`1")]
    public sealed class JsonCollectionInfoValuesImpl<TCollection>
    {
        public JsonCollectionInfoValuesImpl() { }
        public Func<TCollection> ObjectCreator { get; set; }
        public JsonTypeInfo KeyInfo { get; set; }
        public JsonTypeInfo ElementInfo { get; set; }
        public JsonNumberHandling NumberHandling { get; set; }
        public Action<Utf8JsonWriter, TCollection> SerializeHandler { get; set; }
        public JsonPolymorphismOptions PolymorphismOptions { get; set; }
        public JsonTypeClassifierFactory TypeClassifierFactory { get; set; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonParameterInfoValues")]
    public sealed class JsonParameterInfoValuesImpl
    {
        public JsonParameterInfoValuesImpl() { }
        public string Name { get; set; }
        public Type ParameterType { get; set; }
        public int Position { get; set; }
        public bool HasDefaultValue { get; set; }
        public object DefaultValue { get; set; }
        public bool IsNullable { get; set; }
        public bool IsMemberInitializer { get; set; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonDerivedType")]
    public readonly struct JsonDerivedTypeImpl
    {
        public JsonDerivedTypeImpl(Type derivedType)
        {
            DerivedType = derivedType;
            TypeDiscriminator = null;
        }

        public JsonDerivedTypeImpl(Type derivedType, int typeDiscriminator)
        {
            DerivedType = derivedType;
            TypeDiscriminator = typeDiscriminator;
        }

        public JsonDerivedTypeImpl(Type derivedType, string typeDiscriminator)
        {
            DerivedType = derivedType;
            TypeDiscriminator = typeDiscriminator;
        }

        public Type DerivedType { get; }

        public object TypeDiscriminator { get; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions")]
    public class JsonPolymorphismOptionsImpl
    {
        private List<JsonDerivedType> _derivedTypes;
        private string _typeDiscriminatorPropertyName;

        public JsonPolymorphismOptionsImpl() { }

        public IList<JsonDerivedType> DerivedTypes => _derivedTypes ??= new List<JsonDerivedType>();

        internal int DerivedTypeCount => _derivedTypes == null ? 0 : _derivedTypes.Count;

        public bool IgnoreUnrecognizedTypeDiscriminators { get; set; }

        public JsonUnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; set; }

        public string TypeDiscriminatorPropertyName
        {
            get => _typeDiscriminatorPropertyName ?? "$type";
            set => _typeDiscriminatorPropertyName = value;
        }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonTypeInfo")]
    public abstract class JsonTypeInfoImpl
    {
        internal readonly Type _type;
        internal readonly JsonSerializerOptionsImpl _options;
        internal JsonConverterImpl _converter;
        internal JsonTypeInfoKind _kind;
        private List<JsonPropertyInfo> _properties;
        internal Func<JsonSerializerContext, JsonPropertyInfo[]> _propertyInitializer;
        internal Func<JsonParameterInfoValues[]> _parameterInitializer;
        internal JsonPolymorphismOptions _polymorphismOptions;
        internal JsonTypeInfoImpl _elementTypeInfo;
        internal JsonTypeInfoImpl _keyTypeInfo;
        internal Type _elementType;
        internal Type _keyType;
        private bool _configured;
        private bool _configuring;
        private bool _readOnly;

        // Configured object metadata.
        internal JsonPropertyInfoImpl[] _sortedProperties;
        internal Dictionary<string, JsonPropertyInfoImpl> _propertyCache;
        internal JsonPropertyInfoImpl _extensionData;
        internal JsonPropertyInfoImpl[] _requiredProperties;
        internal JsonParameterInfoValues[] _parameters;
        internal JsonPropertyInfoImpl[] _parameterProperties; // by position, the property each parameter matches
        internal int _parameterCount;
        internal JsonPolymorphicTypeResolver _polymorphicResolver;

        internal JsonTypeInfoImpl(Type type, JsonConverterImpl converter, JsonSerializerOptionsImpl options)
        {
            _type = type;
            _converter = converter;
            _options = options;
            _kind = converter == null ? JsonTypeInfoKind.None : converter.Kind;
        }

        public Type Type => _type;

        public JsonConverterImpl Converter => _converter;

        public JsonSerializerOptionsImpl Options => _options;

        public JsonTypeInfoKind Kind => _kind;

        public Type ElementType => _elementType;

        public Type KeyType => _keyType;

        public IJsonTypeInfoResolver OriginatingResolver { get; set; }

        public JsonNumberHandling? NumberHandling { get; set; }

        public JsonUnmappedMemberHandling? UnmappedMemberHandling { get; set; }

        public JsonObjectCreationHandling? PreferredPropertyObjectCreationHandling { get; set; }

        public JsonPolymorphismOptions PolymorphismOptions
        {
            get => _polymorphismOptions;
            set
            {
                VerifyMutable();
                _polymorphismOptions = value;
            }
        }

        public JsonTypeClassifier TypeClassifier { get; set; }

        public Action<object> OnSerializing { get; set; }
        public Action<object> OnSerialized { get; set; }
        public Action<object> OnDeserializing { get; set; }
        public Action<object> OnDeserialized { get; set; }

        public bool IsReadOnly => _readOnly;

        public void MakeReadOnly() => _readOnly = true;

        private void VerifyMutable()
        {
            if (_readOnly) throw new InvalidOperationException("This JsonTypeInfo instance is marked read-only or has already been used in serialization or deserialization.");
        }

        public IList<JsonPropertyInfo> Properties
        {
            get
            {
                if (_properties == null)
                {
                    _properties = new List<JsonPropertyInfo>();
                    if (_kind == JsonTypeInfoKind.Object && _propertyInitializer != null) PopulateProperties();
                }
                return _properties;
            }
        }

        internal abstract Func<object> UntypedCreateObject { get; }

        public Func<object> CreateObject
        {
            get => UntypedCreateObject;
            set => SetUntypedCreateObject(value);
        }

        internal abstract void SetUntypedCreateObject(Func<object> value);

        internal bool HasParameterizedConstructor => _parameterInitializer != null && !(ReflectionSemantics && OnlyMemberInitializers);

        /// <summary>
        /// Metadata standing in for DefaultJsonTypeInfoResolver (reflection). The source generator binds
        /// required and init-only members like constructor parameters (an object initializer), where
        /// reflection constructs the object and then sets them; with this flag such types are read the
        /// reflection way (which, e.g., lets JsonObjectCreationHandling.Populate apply).
        /// </summary>
        internal bool ReflectionSemantics;

        private int _onlyMemberInitializers;

        private bool OnlyMemberInitializers
        {
            get
            {
                if (_onlyMemberInitializers == 0)
                {
                    bool all = true;
                    foreach (JsonParameterInfoValues parameter in _parameters ?? _parameterInitializer())
                        if (!parameter.IsMemberInitializer) all = false;
                    _onlyMemberInitializers = all ? 1 : 2;
                }
                return _onlyMemberInitializers == 1;
            }
        }

        /// <summary>The arguments a parameterized constructor gets for members absent from the JSON.</summary>
        internal object[] DefaultConstructorArguments()
        {
            object[] args = new object[_parameterCount];
            for (int i = 0; i < _parameterCount; i++)
            {
                JsonParameterInfoValues p = _parameters[i];
                args[p.Position] = p.HasDefaultValue ? p.DefaultValue : _parameterProperties[p.Position]?.DefaultValueAsObject;
            }
            return args;
        }

        // Facts about the type the (non-generic) object converter needs.
        internal bool IsValueTypeInfo;
        internal bool IsAbstractTypeInfo;
        /// <summary>
        /// The source generator's fast-path serializer, kept only for types generated in
        /// JsonSourceGenerationMode.Serialization (no property metadata): metadata-based serialization
        /// gives the same output wherever both exist.
        /// </summary>
        internal Action<Utf8JsonWriter, object> _untypedSerializeHandler;

        internal bool SerializationOnly => _untypedSerializeHandler != null && _propertyInitializer == null;

        /// <summary>JsonTypeInfo.CanUseSerializeHandler for a serialization-only type, else the error .NET reports.</summary>
        internal void ThrowIfFastPathUnusable()
        {
            JsonSerializerOptionsImpl options = _options;
            if (JsonSerializerContextImpl.IsCompatibleWithOptions(options.TypeInfoResolver, options)
                || JsonSerializerContextImpl.IsCompatibleWithOptions(OriginatingResolver, options)) return;
            object resolver = options.TypeInfoResolver;
            throw new InvalidOperationException("TypeInfoResolver '" + (resolver == null ? "<null>" : resolver.ToString()) + "' did not provide property metadata for type '" + _type.ToString() + "'.");
        }

        internal Func<object> _untypedCreate;
        internal Func<object[], object> _untypedParameterizedCreate;
        internal bool HasCreateObject => _untypedCreate != null;
        internal object CreateObjectUntyped() => _untypedCreate();
        internal bool HasParameterizedCreator => _untypedParameterizedCreate != null;
        internal object CreateParameterizedUntyped(object[] args) => _untypedParameterizedCreate(args);

        internal bool SupportsPolymorphicDeserialization => _polymorphicResolver != null && _polymorphicResolver.UsesDiscriminators;

        /// <summary>JsonMetadataServices.PopulateProperties: source-generated properties with .NET's run-time filtering.</summary>
        private void PopulateProperties()
        {
            JsonSerializerContext context = _options.TypeInfoResolver as JsonSerializerContext;
            JsonPropertyInfo[] properties = _propertyInitializer(context);
            // Name -> index in _properties.
            Dictionary<string, int> added = new Dictionary<string, int>();
            Dictionary<string, JsonPropertyInfoImpl> ignored = null;
            bool orderSpecified = false;
            foreach (JsonPropertyInfo p in properties)
            {
                JsonPropertyInfoImpl property = (JsonPropertyInfoImpl)(object)p;
                if (!property.SrcGenIsPublic)
                {
                    if (property.SrcGenHasJsonInclude)
                    {
                        if (property.UntypedGetter == null && property.UntypedSetter == null)
                            throw new InvalidOperationException("The non-public property '" + property.MemberName + "' on type '" + property.DeclaringType.ToString() + "' is annotated with 'JsonIncludeAttribute' which is invalid.");
                    }
                    else continue;
                }
                if (!property.SrcGenIsProperty && !property.SrcGenHasJsonInclude && !_options.IncludeFields) continue;

                property.DeclaringTypeInfo = this;
                string memberName = property.MemberName;
                if (added.TryGetValue(property.Name, out int otherIndex))
                {
                    JsonPropertyInfoImpl other = (JsonPropertyInfoImpl)(object)_properties[otherIndex];
                    if (other.IsIgnored)
                    {
                        _properties[otherIndex] = p;
                        orderSpecified |= property.Order != 0;
                    }
                    else
                    {
                        bool ignoreCurrent = property.IsIgnored || property.IsOverriddenOrShadowedBy(other)
                            || (ignored != null && ignored.TryGetValue(memberName, out JsonPropertyInfoImpl ig) && property.IsOverriddenOrShadowedBy(ig));
                        if (!ignoreCurrent)
                            throw new InvalidOperationException("The JSON property name for '" + _type.ToString() + "." + property.Name + "' collides with another property.");
                    }
                }
                else
                {
                    added[property.Name] = _properties.Count;
                    _properties.Add(p);
                    orderSpecified |= property.Order != 0;
                }
                if (property.IsIgnored)
                {
                    ignored ??= new Dictionary<string, JsonPropertyInfoImpl>();
                    ignored[memberName] = property;
                }
            }
            if (orderSpecified) SortByOrder(_properties);
        }

        private static void SortByOrder(List<JsonPropertyInfo> list)
        {
            // Stable insertion sort by Order (List.StableSortByKey in .NET).
            for (int i = 1; i < list.Count; i++)
            {
                JsonPropertyInfo item = list[i];
                int order = ((JsonPropertyInfoImpl)(object)item).Order;
                int j = i - 1;
                while (j >= 0 && ((JsonPropertyInfoImpl)(object)list[j]).Order > order)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = item;
            }
        }

        internal bool IsConfigured => _configured;

        /// <summary>JsonTypeInfo.Configure: resolve property names, converters and constructor parameters once.</summary>
        internal void EnsureConfigured()
        {
            if (_configured || _configuring) return;
            _configuring = true;
            _readOnly = true;
            if (_kind == JsonTypeInfoKind.Object) ConfigureObject();
            if (_polymorphismOptions != null && _polymorphismOptions.DerivedTypes.Count > 0)
                _polymorphicResolver = new JsonPolymorphicTypeResolver(this, _polymorphismOptions);
            ConfigureCore();
            _configuring = false;
            _configured = true;
        }

        internal virtual void ConfigureCore() { }

        private void ConfigureObject()
        {
            IList<JsonPropertyInfo> properties = Properties;
            List<JsonPropertyInfoImpl> sorted = new List<JsonPropertyInfoImpl>();
            List<JsonPropertyInfoImpl> required = null;
            _propertyCache = new Dictionary<string, JsonPropertyInfoImpl>(_options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (JsonPropertyInfo p in properties)
            {
                JsonPropertyInfoImpl property = (JsonPropertyInfoImpl)(object)p;
                property.DeclaringTypeInfo ??= this;
                if (property.IsExtensionData)
                {
                    if (_extensionData != null)
                        throw new InvalidOperationException("The type '" + _type.ToString() + "' cannot have more than one member that has the attribute 'JsonExtensionDataAttribute'.");
                    property.Configure();
                    _extensionData = property;
                    continue;
                }
                property.Configure();
                if (property.IsIgnored) continue;
                sorted.Add(property);
                if (!_propertyCache.TryAdd(property.Name, property))
                    throw new InvalidOperationException("The JSON property name for '" + _type.ToString() + "." + property.Name + "' collides with another property.");
                if (property.IsRequired)
                {
                    required ??= new List<JsonPropertyInfoImpl>();
                    required.Add(property);
                }
            }
            _sortedProperties = sorted.ToArray();
            _requiredProperties = required?.ToArray();

            if (_parameterInitializer != null)
            {
                _parameters = _parameterInitializer();
                _parameterCount = _parameters.Length;
                _parameterProperties = new JsonPropertyInfoImpl[_parameterCount];
                // Parameters bind to the property whose CLR name matches (case-insensitively), like ResolveMatchingParameterInfo.
                Dictionary<string, JsonPropertyInfoImpl> byMemberName = new Dictionary<string, JsonPropertyInfoImpl>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonPropertyInfoImpl property in _sortedProperties)
                    if (property.MemberName != null) byMemberName.TryAdd(property.MemberName, property);
                for (int i = 0; i < _parameterCount; i++)
                {
                    JsonParameterInfoValues parameter = _parameters[i];
                    if (byMemberName.TryGetValue(parameter.Name, out JsonPropertyInfoImpl match) && match.PropertyType == parameter.ParameterType)
                    {
                        _parameterProperties[parameter.Position] = match;
                        match.AssociatedParameterPosition = parameter.Position;
                    }
                }
            }
        }

        /// <summary>Case-sensitivity per options; extension data and unmapped handling are the caller's.</summary>
        internal JsonPropertyInfoImpl GetProperty(string name)
        {
            if (_propertyCache != null && _propertyCache.TryGetValue(name, out JsonPropertyInfoImpl property)) return property;
            return null;
        }

        // Engine entry points (typed in JsonTypeInfoImpl<T>).
        internal abstract void WriteAsObject(Utf8JsonWriterImpl writer, object value, JsonWriteState state);
        internal abstract object ReadAsObject(ref Utf8JsonReaderImpl reader, JsonReadState state);
        internal abstract bool IsNullableOrReference { get; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonTypeInfo`1")]
    public sealed class JsonTypeInfoImpl<T> : JsonTypeInfoImpl
    {
        internal Func<T> _createObject;
        internal Func<object[], T> _parameterizedCreator;
        internal Action<Utf8JsonWriter, T> _serializeHandler;

        internal JsonTypeInfoImpl(JsonConverterImpl converter, JsonSerializerOptionsImpl options)
            : base(typeof(T), converter, options)
        {
            IsValueTypeInfo = CoreIntrinsics.IsValueType<T>();
            IsAbstractTypeInfo = CoreIntrinsics.IsAbstractType<T>();
        }


        internal JsonConverterImpl<T> TypedConverter => (JsonConverterImpl<T>)_converter;

        public new Func<T> CreateObject
        {
            get => _createObject;
            set => SetCreateObject(value);
        }

        internal void SetCreateObject(Func<T> value)
        {
            _createObject = value;
            _untypedCreate = value == null ? null : () => (object)value();
        }

        public Action<Utf8JsonWriter, T> SerializeHandler => _serializeHandler;

        internal override Func<object> UntypedCreateObject
        {
            get
            {
                Func<T> create = _createObject;
                if (create == null) return null;
                return () => (object)create();
            }
        }

        internal override void SetUntypedCreateObject(Func<object> value)
        {
            _createObject = value == null ? null : () => (T)value();
            _untypedCreate = value;
        }

        internal override bool IsNullableOrReference => default(T) == null;

        internal override void WriteAsObject(Utf8JsonWriterImpl writer, object value, JsonWriteState state)
        {
            EnsureConfigured();
            TypedConverter.WriteWithState(writer, value == null ? default(T) : (T)value, _options, state);
        }

        internal override object ReadAsObject(ref Utf8JsonReaderImpl reader, JsonReadState state)
        {
            EnsureConfigured();
            return TypedConverter.ReadWithState(ref reader, typeof(T), _options, state);
        }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonPropertyInfo")]
    public abstract class JsonPropertyInfoImpl
    {
        internal readonly Type _propertyType;
        internal readonly Type _declaringType;
        internal readonly JsonSerializerOptionsImpl _options;
        private string _name;
        internal byte[] _escapedName;      // escaped UTF-8 of Name with the options' encoder
        internal JsonIgnoreCondition? _ignoreCondition;
        internal bool _configured;

        internal JsonPropertyInfoImpl(Type propertyType, Type declaringType, JsonSerializerOptionsImpl options)
        {
            _propertyType = propertyType;
            _declaringType = declaringType;
            _options = options;
        }

        public Type PropertyType => _propertyType;
        public Type DeclaringType => _declaringType;
        public JsonSerializerOptionsImpl Options => _options;

        public string Name
        {
            get => _name;
            set
            {
                if (value == null) throw new ArgumentNullException("value");
                _name = value;
            }
        }

        public int Order { get; set; }
        public bool IsExtensionData { get; set; }
        public bool IsRequired { get; set; }
        public bool IsGetNullable { get; set; } = true;
        public bool IsSetNullable { get; set; } = true;
        public JsonNumberHandling? NumberHandling { get; set; }
        public JsonObjectCreationHandling? ObjectCreationHandling { get; set; }
        public ICustomAttributeProvider AttributeProvider { get; set; }
        public Func<object, object, bool> ShouldSerialize { get; set; }

        public abstract JsonConverterImpl CustomConverter { get; set; }
        public abstract Func<object, object> Get { get; set; }
        public abstract Action<object, object> Set { get; set; }

        internal string MemberName;
        internal bool SrcGenIsPublic = true;
        internal bool SrcGenHasJsonInclude;
        internal bool SrcGenIsProperty = true;
        internal JsonTypeInfoImpl DeclaringTypeInfo;
        internal int AssociatedParameterPosition = -1;

        // Configured state.
        internal bool CanSerialize;
        internal bool CanDeserialize;
        internal bool IgnoreDefaultValuesOnWrite;
        internal bool IgnoreNullTokensOnRead;
        internal JsonNumberHandling? EffectiveNumberHandling;

        internal abstract object UntypedGetter { get; }
        internal abstract object UntypedSetter { get; }

        internal bool IsIgnored => _ignoreCondition == JsonIgnoreCondition.Always && UntypedGetter == null && UntypedSetter == null;

        internal bool IsOverriddenOrShadowedBy(JsonPropertyInfoImpl other) =>
            MemberName == other.MemberName && (_declaringType == other._declaringType || _declaringType == null || other._declaringType == null || CoreIntrinsics.IsTypeAssignableTo(other._declaringType, _declaringType));

        internal abstract void Configure();

        internal abstract bool PropertyTypeCanBeNull { get; }

        internal void ConfigureCommon(bool hasGetter, bool hasSetter, bool isCollection)
        {
            CanSerialize = hasGetter;
            CanDeserialize = hasSetter;
            if (_ignoreCondition == null)
            {
                // Read-only non-collection members follow IgnoreReadOnlyProperties/Fields.
                if (!isCollection && hasGetter && !hasSetter)
                {
                    bool ignoreReadOnly = SrcGenIsProperty ? _options.IgnoreReadOnlyProperties : _options.IgnoreReadOnlyFields;
                    if (ignoreReadOnly && ShouldSerialize == null) CanSerialize = false;
                }
                if (isCollection && !hasGetter && hasSetter) CanDeserialize = false;
#pragma warning disable SYSLIB0020
                if (_options.IgnoreNullValues)
#pragma warning restore SYSLIB0020
                {
                    if (PropertyTypeCanBeNull)
                    {
                        IgnoreNullTokensOnRead = !IsRequired;
                        IgnoreDefaultValuesOnWrite = ShouldSerialize == null;
                    }
                }
                else if (_options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingNull)
                {
                    if (PropertyTypeCanBeNull) IgnoreDefaultValuesOnWrite = ShouldSerialize == null;
                }
                else if (_options.DefaultIgnoreCondition == JsonIgnoreCondition.WhenWritingDefault)
                {
                    IgnoreDefaultValuesOnWrite = ShouldSerialize == null;
                }
            }
            else
            {
                switch (_ignoreCondition.Value)
                {
                    case JsonIgnoreCondition.Always:
                        CanSerialize = false;
                        CanDeserialize = false;
                        break;
                    case JsonIgnoreCondition.WhenWritingDefault:
                        IgnoreDefaultValuesOnWrite = true;
                        break;
                    case JsonIgnoreCondition.WhenWritingNull:
                        if (PropertyTypeCanBeNull) IgnoreDefaultValuesOnWrite = true;
                        else
                            throw new InvalidOperationException("The ignore condition 'JsonIgnoreCondition.WhenWritingNull' is not valid on value-type member '" + MemberName + "' on type '" + _declaringType.ToString() + "'. Consider using 'JsonIgnoreCondition.WhenWritingDefault'.");
                        break;
                    case (JsonIgnoreCondition)4: // WhenWriting
                        CanSerialize = false;
                        break;
                    case (JsonIgnoreCondition)5: // WhenReading
                        CanDeserialize = false;
                        break;
                }
            }
            byte[] utf8 = JsonUtf8.FromUtf16(_name, out int bad);
            if (utf8 == null) throw new ArgumentException(JsonSR.CannotTranscodeInvalidUtf16);
            _escapedName = JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(utf8, 0, utf8.Length, _options.Encoder));
            _configured = true;
        }

        internal bool Populate;

        /// <summary>JsonPropertyInfo.DetermineEffectiveObjectCreationHandlingForProperty.</summary>
        internal void DetermineObjectCreationHandling(bool converterCanPopulate, JsonTypeInfoImpl propertyTypeInfo, bool valueType)
        {
            JsonTypeInfoImpl declaring = DeclaringTypeInfo;
            bool parameterized = declaring != null && declaring.HasParameterizedConstructor;
            bool ignoreReadOnly = SrcGenIsProperty ? _options.IgnoreReadOnlyProperties : _options.IgnoreReadOnlyFields;
            bool populate = false;
            if (ObjectCreationHandling == null)
            {
                JsonObjectCreationHandling preferred = declaring?.PreferredPropertyObjectCreationHandling
                    ?? (parameterized ? JsonObjectCreationHandling.Replace : _options.PreferredObjectCreationHandling);
                populate = preferred == JsonObjectCreationHandling.Populate && converterCanPopulate && UntypedGetter != null
                    && (!valueType || UntypedSetter != null) && !(declaring != null && declaring.SupportsPolymorphicDeserialization) && !(UntypedSetter == null && ignoreReadOnly);
            }
            else if (ObjectCreationHandling == JsonObjectCreationHandling.Populate)
            {
                string where = "Property '" + Name + "' on type '" + _declaringType.ToString() + "' is marked with JsonObjectCreationHandling.Populate but ";
                if (!converterCanPopulate)
                    throw new InvalidOperationException(where + "it doesn't support populating. This can be either because the property type is immutable or it could use a custom converter.");
                if (UntypedGetter == null) throw new InvalidOperationException(where + "it doesn't have a getter.");
                if (valueType && UntypedSetter == null) throw new InvalidOperationException(where + "is a value type that doesn't have a setter.");
                if (propertyTypeInfo != null && propertyTypeInfo.SupportsPolymorphicDeserialization)
                    throw new InvalidOperationException(where + "its type allows polymorphic deserialization.");
                if (UntypedSetter == null && ignoreReadOnly)
                    throw new InvalidOperationException(where + "it is a read-only member and JsonSerializerOptions has IgnoreReadOnlyProperties or IgnoreReadOnlyFields set.");
                populate = true;
            }
            if (populate)
            {
                if (parameterized) throw new NotSupportedException("JsonObjectCreationHandling.Populate is currently not supported in types with parameterized constructors.");
                if (_options.ReferenceHandler != null) throw new InvalidOperationException("JsonObjectCreationHandling.Populate is incompatible with reference handling.");
                Populate = true;
                // A getter is enough to populate a read-only member (unless it's ignored outright).
                if (_ignoreCondition != JsonIgnoreCondition.Always && _ignoreCondition != (JsonIgnoreCondition)5) CanDeserialize = true;
            }
        }

        // Engine.
        internal abstract void WriteMember(Utf8JsonWriterImpl writer, object obj, JsonWriteState state);
        internal abstract void ReadAndSetMember(ref Utf8JsonReaderImpl reader, object obj, JsonReadState state);
        internal abstract object ReadValueAsObject(ref Utf8JsonReaderImpl reader, JsonReadState state);
        internal abstract object GetValueAsObject(object obj);
        internal abstract void SetValueAsObject(object obj, object value);
        internal abstract object DefaultValueAsObject { get; }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1")]
    internal sealed class JsonPropertyInfoImpl<T> : JsonPropertyInfoImpl
    {
        internal Func<object, T> _getter;
        internal Action<object, T> _setter;
        internal JsonConverterImpl<T> _customConverter;
        internal JsonConverterImpl<T> _effectiveConverter;
        internal JsonTypeInfoImpl<T> _propertyTypeInfo;
        private Func<object, object> _untypedGet;
        private Action<object, object> _untypedSet;

        internal JsonPropertyInfoImpl(Type declaringType, JsonSerializerOptionsImpl options)
            : base(typeof(T), declaringType, options)
        {
        }

        public override JsonConverterImpl CustomConverter
        {
            get => _customConverter;
            set => _customConverter = (JsonConverterImpl<T>)value;
        }

        public override Func<object, object> Get
        {
            get
            {
                if (_untypedGet == null && _getter != null)
                {
                    Func<object, T> getter = _getter;
                    _untypedGet = obj => (object)getter(obj);
                }
                return _untypedGet;
            }
            set
            {
                _untypedGet = value;
                _getter = value == null ? null : obj => (T)value(obj);
            }
        }

        public override Action<object, object> Set
        {
            get
            {
                if (_untypedSet == null && _setter != null)
                {
                    Action<object, T> setter = _setter;
                    _untypedSet = (obj, v) => setter(obj, (T)v);
                }
                return _untypedSet;
            }
            set
            {
                _untypedSet = value;
                _setter = value == null ? null : (obj, v) => value(obj, (object)v);
            }
        }

        internal override object UntypedGetter => _getter;
        internal override object UntypedSetter => _setter;

        internal override bool PropertyTypeCanBeNull => default(T) == null;

        internal override object DefaultValueAsObject => default(T);

        internal override void Configure()
        {
            if (_configured) return;
            if (IsIgnored)
            {
                CanSerialize = false;
                CanDeserialize = false;
                _configured = true;
                return;
            }
            if (_customConverter != null) _effectiveConverter = _customConverter;
            else
            {
                _propertyTypeInfo ??= (JsonTypeInfoImpl<T>)_options.GetTypeInfoForRuntimeType(typeof(T));
                _propertyTypeInfo.EnsureConfigured();
                _effectiveConverter = _propertyTypeInfo.TypedConverter;
            }
            JsonTypeInfoKind kind = _effectiveConverter.Kind;
            ConfigureCommon(_getter != null, _setter != null, kind == JsonTypeInfoKind.Enumerable || kind == JsonTypeInfoKind.Dictionary);
            DetermineObjectCreationHandling(_effectiveConverter.CanPopulate, _propertyTypeInfo, CoreIntrinsics.IsValueType<T>());
            // Number handling: attribute on the member, then its declaring type, then the property type, then the options.
            if (_effectiveConverter.IsNumberHandlingApplicable)
            {
                JsonNumberHandling? handling = NumberHandling ?? DeclaringTypeInfo?.NumberHandling ?? _propertyTypeInfo?.NumberHandling;
                if (!handling.HasValue && _options.NumberHandling != JsonNumberHandling.Strict) handling = _options.NumberHandling;
                EffectiveNumberHandling = handling;
            }
        }

        internal override void WriteMember(Utf8JsonWriterImpl writer, object obj, JsonWriteState state)
        {
            T value = _getter(obj);
            if (IgnoreDefaultValuesOnWrite)
            {
                if (default(T) == null ? value == null : CoreIntrinsics.AreEqual(value, default(T))) return;
            }
            if (ShouldSerialize != null && !ShouldSerialize(obj, (object)value)) return;
            writer.WritePropertyNameUtf8Escaped(_escapedName);
            state.Push(Name);
            JsonNumberHandling? saved = state.NumberHandling;
            state.NumberHandling = EffectiveNumberHandling;
            _effectiveConverter.WriteWithState(writer, value, _options, state);
            state.NumberHandling = saved;
            state.Pop();
        }

        internal T ReadValue(ref Utf8JsonReaderImpl reader, JsonReadState state)
        {
            JsonNumberHandling? saved = state.NumberHandling;
            state.NumberHandling = EffectiveNumberHandling;
            state.CurrentValueType = typeof(T);
            T value = _effectiveConverter.ReadWithState(ref reader, typeof(T), _options, state);
            state.NumberHandling = saved;
            return value;
        }


        internal override void ReadAndSetMember(ref Utf8JsonReaderImpl reader, object obj, JsonReadState state)
        {
            if (reader.TokenType == JsonTokenType.Null && IgnoreNullTokensOnRead) return;
            if (Populate && reader.TokenType != JsonTokenType.Null)
            {
                T current = _getter(obj);
                if (current != null)
                {
                    JsonNumberHandling? saved = state.NumberHandling;
                    state.NumberHandling = EffectiveNumberHandling;
                    state.CurrentValueType = typeof(T);
                    T populated = _effectiveConverter.ReadInto(ref reader, current, _options, state);
                    state.NumberHandling = saved;
                    if (CoreIntrinsics.IsValueType<T>()) _setter(obj, populated);
                    return;
                }
                if (_setter == null)
                {
                    reader.Skip();
                    return;
                }
            }
            T value = ReadValue(ref reader, state);
            if (!IsSetNullable && value == null && _options.RespectNullableAnnotations)
                throw new JsonExceptionImpl("The property or field '" + Name + "' on type '" + _declaringType.ToString() + "' doesn't allow setting null values. Consider updating its nullability annotation.") { AppendPathInformation = true };
            _setter(obj, value);
        }

        internal override object ReadValueAsObject(ref Utf8JsonReaderImpl reader, JsonReadState state) => ReadValue(ref reader, state);

        internal override object GetValueAsObject(object obj) => _getter(obj);

        internal override void SetValueAsObject(object obj, object value) => _setter(obj, (T)value);
    }

    /// <summary>[JsonDerivedType]/[JsonPolymorphic]: discriminators for serialization and deserialization.</summary>
    internal sealed class JsonPolymorphicTypeResolver
    {
        private readonly JsonTypeInfoImpl _baseInfo;
        internal readonly string DiscriminatorName;
        internal readonly byte[] EscapedDiscriminatorName;
        internal readonly bool IgnoreUnrecognized;
        internal readonly JsonUnknownDerivedTypeHandling UnknownHandling;
        private readonly List<Type> _types = new List<Type>();
        private readonly List<object> _discriminators = new List<object>();

        public JsonPolymorphicTypeResolver(JsonTypeInfoImpl baseInfo, JsonPolymorphismOptions options)
        {
            _baseInfo = baseInfo;
            DiscriminatorName = options.TypeDiscriminatorPropertyName;
            IgnoreUnrecognized = options.IgnoreUnrecognizedTypeDiscriminators;
            UnknownHandling = options.UnknownDerivedTypeHandling;
            byte[] utf8 = JsonUtf8.FromUtf16(DiscriminatorName, out int bad);
            EscapedDiscriminatorName = JsonWriterHelper.ToUtf8Valid(JsonWriterHelper.EscapeUtf8(utf8, 0, utf8.Length, baseInfo._options.Encoder));
            foreach (JsonDerivedType derived in options.DerivedTypes)
            {
                _types.Add(derived.DerivedType);
                _discriminators.Add(derived.TypeDiscriminator);
            }
        }

        public bool UsesDiscriminators
        {
            get
            {
                foreach (object d in _discriminators) if (d != null) return true;
                return false;
            }
        }

        /// <summary>The derived type registered for a run-time type (exact match, as .NET checks registered types).</summary>
        public bool TryGetDerivedType(Type runtimeType, out Type derived, out object discriminator)
        {
            for (int i = 0; i < _types.Count; i++)
            {
                if (_types[i] == runtimeType)
                {
                    derived = _types[i];
                    discriminator = _discriminators[i];
                    return true;
                }
            }
            derived = null;
            discriminator = null;
            return false;
        }

        /// <summary>
        /// JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor (PolymorphicTypeResolver.CalculateNearestAncestor):
        /// the closest registered base class, then registered interfaces (two different ones are a diamond).
        /// </summary>
        public bool TryGetNearestAncestor(Type runtimeType, Type baseType, out Type ancestor, out object discriminator)
        {
            int best = -1;
            for (int i = 0; i < _types.Count; i++)
            {
                Type candidate = _types[i];
                if (candidate == baseType || candidate == runtimeType || CoreIntrinsics.IsInterfaceTypeObject(candidate)) continue;
                if (!CoreIntrinsics.IsTypeAssignableTo(runtimeType, candidate)) continue;
                if (best < 0 || CoreIntrinsics.IsTypeAssignableTo(candidate, _types[best])) best = i;
            }
            if (CoreIntrinsics.IsInterfaceTypeObject(baseType))
            {
                for (int i = 0; i < _types.Count; i++)
                {
                    Type candidate = _types[i];
                    if (candidate == baseType || !CoreIntrinsics.IsInterfaceTypeObject(candidate) || !CoreIntrinsics.IsTypeAssignableTo(runtimeType, candidate)) continue;
                    if (best < 0) best = i;
                    else if (best != i)
                        throw new NotSupportedException("Runtime type '" + runtimeType.ToString() + "' has a diamond ambiguity between derived types '" + _types[best].ToString() + "' and '" + candidate.ToString()
                            + "' of polymorphic type '" + baseType.ToString() + "'. Consider either removing one of the derived types or removing the 'JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor' setting.");
                }
            }
            ancestor = best < 0 ? null : _types[best];
            discriminator = best < 0 ? null : _discriminators[best];
            return best >= 0;
        }

        public bool TryGetTypeByDiscriminator(string discriminator, out Type type)
        {
            for (int i = 0; i < _types.Count; i++)
            {
                object d = _discriminators[i];
                if (d is string s && s == discriminator)
                {
                    type = _types[i];
                    return true;
                }
            }
            type = null;
            return false;
        }

        public bool TryGetTypeByDiscriminator(int discriminator, out Type type)
        {
            for (int i = 0; i < _types.Count; i++)
            {
                if (_discriminators[i] is int n && n == discriminator)
                {
                    type = _types[i];
                    return true;
                }
            }
            type = null;
            return false;
        }
    }

    [Shadows("System.Text.Json.Serialization.Metadata.JsonMetadataServices")]
    public static class JsonMetadataServicesImpl
    {
        private static JsonConverterImpl<bool> s_boolean;
        private static JsonConverterImpl<byte[]> s_byteArray;
        private static JsonConverterImpl<byte> s_byte;
        private static JsonConverterImpl<char> s_char;
        private static JsonConverterImpl<DateTime> s_dateTime;
        private static JsonConverterImpl<DateTimeOffset> s_dateTimeOffset;
        private static JsonConverterImpl<decimal> s_decimal;
        private static JsonConverterImpl<double> s_double;
        private static JsonConverterImpl<short> s_int16;
        private static JsonConverterImpl<int> s_int32;
        private static JsonConverterImpl<long> s_int64;
        private static JsonConverterImpl<object> s_object;
        private static JsonConverterImpl<sbyte> s_sbyte;
        private static JsonConverterImpl<float> s_single;
        private static JsonConverterImpl<string> s_string;
        private static JsonConverterImpl<TimeSpan> s_timeSpan;
        private static JsonConverterImpl<ushort> s_uint16;
        private static JsonConverterImpl<uint> s_uint32;
        private static JsonConverterImpl<ulong> s_uint64;
        private static JsonConverterImpl<JsonElement> s_jsonElement;
        private static JsonConverterImpl<JsonDocument> s_jsonDocument;
        private static JsonConverterImpl<System.Text.Json.Nodes.JsonNode> s_jsonNode;
        private static JsonConverterImpl<System.Text.Json.Nodes.JsonObject> s_jsonObject;
        private static JsonConverterImpl<System.Text.Json.Nodes.JsonArray> s_jsonArray;
        private static JsonConverterImpl<System.Text.Json.Nodes.JsonValue> s_jsonValue;

        public static JsonConverterImpl<bool> BooleanConverter => s_boolean ??= new BooleanConverter();
        public static JsonConverterImpl<byte[]> ByteArrayConverter => s_byteArray ??= new ByteArrayConverter();
        public static JsonConverterImpl<byte> ByteConverter => s_byte ??= new ByteConverter();
        public static JsonConverterImpl<char> CharConverter => s_char ??= new CharConverter();
        public static JsonConverterImpl<DateTime> DateTimeConverter => s_dateTime ??= new DateTimeConverter();
        public static JsonConverterImpl<DateTimeOffset> DateTimeOffsetConverter => s_dateTimeOffset ??= new DateTimeOffsetConverter();
        public static JsonConverterImpl<decimal> DecimalConverter => s_decimal ??= new DecimalConverter();
        public static JsonConverterImpl<double> DoubleConverter => s_double ??= new DoubleConverter();
        public static JsonConverterImpl<short> Int16Converter => s_int16 ??= new Int16Converter();
        public static JsonConverterImpl<int> Int32Converter => s_int32 ??= new Int32Converter();
        public static JsonConverterImpl<long> Int64Converter => s_int64 ??= new Int64Converter();
        public static JsonConverterImpl<object> ObjectConverter => s_object ??= new ObjectConverter();
        public static JsonConverterImpl<sbyte> SByteConverter => s_sbyte ??= new SByteConverter();
        public static JsonConverterImpl<float> SingleConverter => s_single ??= new SingleConverter();
        public static JsonConverterImpl<string> StringConverter => s_string ??= new StringConverter();
        public static JsonConverterImpl<TimeSpan> TimeSpanConverter => s_timeSpan ??= new TimeSpanConverter();
        private static JsonConverterImpl<Guid> s_guid;
        public static JsonConverterImpl<Guid> GuidConverter => s_guid ??= new GuidConverter();
        public static JsonConverterImpl<ushort> UInt16Converter => s_uint16 ??= new UInt16Converter();
        public static JsonConverterImpl<uint> UInt32Converter => s_uint32 ??= new UInt32Converter();
        public static JsonConverterImpl<ulong> UInt64Converter => s_uint64 ??= new UInt64Converter();
        public static JsonConverterImpl<JsonElement> JsonElementConverter => s_jsonElement ??= new JsonElementConverter();
        public static JsonConverterImpl<JsonDocument> JsonDocumentConverter => s_jsonDocument ??= new JsonDocumentConverter();
        public static JsonConverterImpl<System.Text.Json.Nodes.JsonNode> JsonNodeConverter => s_jsonNode ??= new JsonNodeConverter();
        public static JsonConverterImpl<System.Text.Json.Nodes.JsonObject> JsonObjectConverter => s_jsonObject ??= new JsonNodeSubtypeConverter<System.Text.Json.Nodes.JsonObject>();
        public static JsonConverterImpl<System.Text.Json.Nodes.JsonArray> JsonArrayConverter => s_jsonArray ??= new JsonNodeSubtypeConverter<System.Text.Json.Nodes.JsonArray>();
        public static JsonConverterImpl<System.Text.Json.Nodes.JsonValue> JsonValueConverter => s_jsonValue ??= new JsonNodeSubtypeConverter<System.Text.Json.Nodes.JsonValue>();

        public static JsonConverterImpl<T> GetUnsupportedTypeConverter<T>() => new UnsupportedTypeConverter<T>();

        public static JsonConverterImpl<T> GetEnumConverter<T>(JsonSerializerOptionsImpl options) where T : struct, Enum
        {
            if (options == null) throw new ArgumentNullException("options");
            return new EnumConverter<T>(EnumConverterOptions.AllowNumbers, null, options);
        }

        public static JsonConverterImpl<T?> GetNullableConverter<T>(JsonSerializerOptionsImpl options) where T : struct
        {
            if (options == null) throw new ArgumentNullException("options");
            return new NullableConverter<T>(null, options);
        }

        public static JsonConverterImpl<T?> GetNullableConverter<T>(JsonTypeInfoImpl<T> underlyingTypeInfo) where T : struct
        {
            if (underlyingTypeInfo == null) throw new ArgumentNullException("underlyingTypeInfo");
            return new NullableConverter<T>(underlyingTypeInfo, underlyingTypeInfo._options);
        }

        public static JsonTypeInfoImpl<T> CreateValueInfo<T>(JsonSerializerOptionsImpl options, JsonConverterImpl converter)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (converter == null) throw new ArgumentNullException("converter");
            JsonConverterImpl<T> typed = converter as JsonConverterImpl<T>;
            if (typed == null) typed = new CastingConverter<T>(converter);
            return new JsonTypeInfoImpl<T>(typed, options);
        }

        public static JsonTypeInfoImpl<T> CreateObjectInfo<T>(JsonSerializerOptionsImpl options, JsonObjectInfoValuesImpl<T> objectInfo)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (objectInfo == null) throw new ArgumentNullException("objectInfo");
            ObjectConverter<T> converter = new ObjectConverter<T>();
            JsonTypeInfoImpl<T> info = new JsonTypeInfoImpl<T>(converter, options);
            converter.TypeInfo = info;
            info.SetCreateObject(objectInfo.ObjectCreator);
            info._parameterizedCreator = objectInfo.ObjectWithParameterizedConstructorCreator;
            Func<object[], T> parameterized = objectInfo.ObjectWithParameterizedConstructorCreator;
            if (parameterized != null) info._untypedParameterizedCreate = args => (object)parameterized(args);
            info._propertyInitializer = objectInfo.PropertyMetadataInitializer;
            info._parameterInitializer = objectInfo.ConstructorParameterMetadataInitializer;
            info._serializeHandler = objectInfo.SerializeHandler;
            Action<Utf8JsonWriter, T> handler = objectInfo.SerializeHandler;
            if (handler != null && objectInfo.PropertyMetadataInitializer == null)
                info._untypedSerializeHandler = (writer, value) => handler(writer, (T)value);
            info._polymorphismOptions = objectInfo.PolymorphismOptions;
            info.NumberHandling = objectInfo.NumberHandling == JsonNumberHandling.Strict ? (JsonNumberHandling?)null : objectInfo.NumberHandling;
            if (info._createObject == null && info._parameterizedCreator == null && info._parameterInitializer == null && info._propertyInitializer != null)
                converter.NoConstructor = true;
            return info;
        }

        public static JsonPropertyInfoImpl CreatePropertyInfo<T>(JsonSerializerOptionsImpl options, JsonPropertyInfoValuesImpl<T> propertyInfo)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (propertyInfo == null) throw new ArgumentNullException("propertyInfo");
            Type declaringType = propertyInfo.DeclaringType;
            string propertyName = propertyInfo.PropertyName;
            JsonPropertyInfoImpl<T> info = new JsonPropertyInfoImpl<T>(declaringType, options);
            // DeterminePropertyName: [JsonPropertyName] wins over the naming policy.
            string name;
            if (propertyInfo.JsonPropertyName != null) name = propertyInfo.JsonPropertyName;
            else if (options.PropertyNamingPolicy == null) name = propertyName;
            else name = options.PropertyNamingPolicy.ConvertName(propertyName);
            if (name == null)
                throw new InvalidOperationException("The JSON property name for '" + declaringType.ToString() + "." + propertyName + "' cannot be null.");
            info.Name = name;
            info.MemberName = propertyName;
            info.SrcGenIsProperty = propertyInfo.IsProperty;
            info.SrcGenIsPublic = propertyInfo.IsPublic;
            info.SrcGenHasJsonInclude = propertyInfo.HasJsonInclude;
            info.IsExtensionData = propertyInfo.IsExtensionData;
            info._customConverter = propertyInfo.Converter == null ? null : (JsonConverterImpl<T>)(object)propertyInfo.Converter;
            if (propertyInfo.IgnoreCondition != JsonIgnoreCondition.Always)
            {
                info._getter = propertyInfo.Getter;
                info._setter = propertyInfo.Setter;
            }
            info._ignoreCondition = propertyInfo.IgnoreCondition;
            if (propertyInfo.PropertyTypeInfo != null) info._propertyTypeInfo = (JsonTypeInfoImpl<T>)(object)propertyInfo.PropertyTypeInfo;
            info.NumberHandling = propertyInfo.NumberHandling;
            return info;
        }

        // ---------------------------------------------------------------- collections

        private static JsonTypeInfoImpl<TCollection> Collection<TCollection, TElement>(JsonSerializerOptionsImpl options,
            JsonCollectionInfoValuesImpl<TCollection> info, CollectionConverter<TCollection, TElement> converter)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (info == null) throw new ArgumentNullException("collectionInfo");
            JsonTypeInfoImpl<TCollection> typeInfo = new JsonTypeInfoImpl<TCollection>(converter, options);
            converter.TypeInfo = typeInfo;
            converter.Create = info.ObjectCreator;
            typeInfo.SetCreateObject(info.ObjectCreator);
            typeInfo._serializeHandler = info.SerializeHandler;
            typeInfo._polymorphismOptions = info.PolymorphismOptions;
            typeInfo._elementType = typeof(TElement);
            if (info.ElementInfo != null) typeInfo._elementTypeInfo = (JsonTypeInfoImpl)(object)info.ElementInfo;
            typeInfo.NumberHandling = info.NumberHandling == JsonNumberHandling.Strict ? (JsonNumberHandling?)null : info.NumberHandling;
            return typeInfo;
        }

        private static JsonTypeInfoImpl<TCollection> UnsupportedCollection<TCollection>(JsonSerializerOptionsImpl options,
            JsonCollectionInfoValuesImpl<TCollection> info, string what)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (info == null) throw new ArgumentNullException("collectionInfo");
            JsonTypeInfoImpl<TCollection> typeInfo = new JsonTypeInfoImpl<TCollection>(new CsharpcUnsupportedConverter<TCollection>(what), options);
            typeInfo.SetCreateObject(info.ObjectCreator);
            return typeInfo;
        }

        private static JsonTypeInfoImpl<TCollection> Dictionary<TCollection, TKey, TValue>(JsonSerializerOptionsImpl options,
            JsonCollectionInfoValuesImpl<TCollection> info, DictionaryConverter<TCollection, TKey, TValue> converter)
            where TCollection : IEnumerable<KeyValuePair<TKey, TValue>>
        {
            if (options == null) throw new ArgumentNullException("options");
            if (info == null) throw new ArgumentNullException("collectionInfo");
            JsonTypeInfoImpl<TCollection> typeInfo = new JsonTypeInfoImpl<TCollection>(converter, options);
            converter.TypeInfo = typeInfo;
            converter.Create = info.ObjectCreator;
            typeInfo.SetCreateObject(info.ObjectCreator);
            typeInfo._serializeHandler = info.SerializeHandler;
            typeInfo._polymorphismOptions = info.PolymorphismOptions;
            typeInfo._elementType = typeof(TValue);
            typeInfo._keyType = typeof(TKey);
            typeInfo.NumberHandling = info.NumberHandling == JsonNumberHandling.Strict ? (JsonNumberHandling?)null : info.NumberHandling;
            return typeInfo;
        }

        public static JsonTypeInfoImpl<TElement[]> CreateArrayInfo<TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TElement[]> collectionInfo) =>
            Collection(options, collectionInfo, new ArrayConverter<TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateListInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : List<TElement> =>
            Collection(options, collectionInfo, new ListConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateIListInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : IList<TElement> =>
            Collection(options, collectionInfo, new ICollectionOfTConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateICollectionInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : ICollection<TElement> =>
            Collection(options, collectionInfo, new ICollectionOfTConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateISetInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : ISet<TElement> =>
            Collection(options, collectionInfo, new ICollectionOfTConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateIEnumerableInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : IEnumerable<TElement> =>
            Collection(options, collectionInfo, new IEnumerableOfTConverter<TCollection, TElement>());

        // Non-generic collections (System.Collections.IEnumerable / IList / IDictionary): metadata the source
        // generator emits for every reachable property type, even unused ones (System.Data's collections).
        // Serializing them is reported, not guessed.
        public static JsonTypeInfoImpl<TCollection> CreateIEnumerableInfo<TCollection>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : System.Collections.IEnumerable =>
            UnsupportedCollection(options, collectionInfo, "a non-generic IEnumerable");

        public static JsonTypeInfoImpl<TCollection> CreateIListInfo<TCollection>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : System.Collections.IList =>
            UnsupportedCollection(options, collectionInfo, "a non-generic IList");

        public static JsonTypeInfoImpl<TCollection> CreateIDictionaryInfo<TCollection>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : System.Collections.IDictionary =>
            UnsupportedCollection(options, collectionInfo, "a non-generic IDictionary");

        public static JsonTypeInfoImpl<TCollection> CreateIReadOnlySetInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : IReadOnlySet<TElement> =>
            Collection(options, collectionInfo, new IEnumerableOfTConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateQueueInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : Queue<TElement> =>
            Collection(options, collectionInfo, new QueueConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateStackInfo<TCollection, TElement>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : Stack<TElement> =>
            Collection(options, collectionInfo, new StackConverter<TCollection, TElement>());

        public static JsonTypeInfoImpl<TCollection> CreateImmutableEnumerableInfo<TCollection, TElement>(JsonSerializerOptionsImpl options,
            JsonCollectionInfoValuesImpl<TCollection> collectionInfo, Func<IEnumerable<TElement>, TCollection> createRangeFunc)
            where TCollection : IEnumerable<TElement>
        {
            IEnumerableOfTConverter<TCollection, TElement> converter = new IEnumerableOfTConverter<TCollection, TElement>();
            converter.CreateRange = createRangeFunc;
            return Collection(options, collectionInfo, converter);
        }

        public static JsonTypeInfoImpl<TCollection> CreateDictionaryInfo<TCollection, TKey, TValue>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : Dictionary<TKey, TValue> =>
            Dictionary(options, collectionInfo, new DictionaryConverter<TCollection, TKey, TValue>());

        public static JsonTypeInfoImpl<TCollection> CreateIDictionaryInfo<TCollection, TKey, TValue>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : IDictionary<TKey, TValue> =>
            Dictionary(options, collectionInfo, new DictionaryConverter<TCollection, TKey, TValue>());

        public static JsonTypeInfoImpl<TCollection> CreateIReadOnlyDictionaryInfo<TCollection, TKey, TValue>(JsonSerializerOptionsImpl options, JsonCollectionInfoValuesImpl<TCollection> collectionInfo)
            where TCollection : IReadOnlyDictionary<TKey, TValue> =>
            Dictionary(options, collectionInfo, new DictionaryConverter<TCollection, TKey, TValue>());

        public static JsonTypeInfoImpl<TCollection> CreateImmutableDictionaryInfo<TCollection, TKey, TValue>(JsonSerializerOptionsImpl options,
            JsonCollectionInfoValuesImpl<TCollection> collectionInfo, Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> createRangeFunc)
            where TCollection : IReadOnlyDictionary<TKey, TValue>
        {
            DictionaryConverter<TCollection, TKey, TValue> converter = new DictionaryConverter<TCollection, TKey, TValue>();
            converter.CreateRange = createRangeFunc;
            return Dictionary(options, collectionInfo, converter);
        }
    }
}
