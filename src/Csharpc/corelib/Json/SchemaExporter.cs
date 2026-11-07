// System.Text.Json.Schema.JsonSchemaExporter: JSON schemas generated from serialization contracts, the way
// Microsoft.Extensions.AI describes function parameters. The contracts are the JsonTypeInfo metadata of
// Serializer*.cs; each built-in converter supplies its own schema through JsonConverterImpl.GetSchema.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Schema
// (release/11.0): JsonSchemaExporter.cs, JsonSchema.cs, JsonSchemaType.cs, JsonSchemaExporterContext.cs,
// JsonSchemaExporterOptions.cs.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CsharpcCoreLib
{
    [Flags]
    internal enum JsonSchemaType
    {
        Any = 0,
        Null = 1,
        Boolean = 2,
        Integer = 4,
        Number = 8,
        String = 16,
        Object = 32,
        Array = 64,
    }

    internal sealed class JsonSchema
    {
        internal const string RefPropertyName = "$ref";
        internal const string CommentPropertyName = "$comment";
        internal const string TypePropertyName = "type";
        internal const string FormatPropertyName = "format";
        internal const string ContentEncodingPropertyName = "contentEncoding";
        internal const string PatternPropertyName = "pattern";
        internal const string PropertiesPropertyName = "properties";
        internal const string RequiredPropertyName = "required";
        internal const string ItemsPropertyName = "items";
        internal const string AdditionalPropertiesPropertyName = "additionalProperties";
        internal const string EnumPropertyName = "enum";
        internal const string NotPropertyName = "not";
        internal const string AnyOfPropertyName = "anyOf";
        internal const string ConstPropertyName = "const";
        internal const string DefaultPropertyName = "default";
        internal const string DeprecatedPropertyName = "deprecated";
        internal const string MinLengthPropertyName = "minLength";
        internal const string MaxLengthPropertyName = "maxLength";

        public static JsonSchema CreateFalseSchema() => new JsonSchema(false);
        public static JsonSchema CreateTrueSchema() => new JsonSchema(true);

        public JsonSchema() { }
        private JsonSchema(bool trueOrFalse) { _trueOrFalse = trueOrFalse; }

        public bool IsTrue => _trueOrFalse == true;
        public bool IsFalse => _trueOrFalse == false;

        // A JSON schema is an object or a boolean; false and true schemas admit no keywords.
        private readonly bool? _trueOrFalse;

        private string _ref;
        private string _comment;
        private JsonSchemaType _type = JsonSchemaType.Any;
        private string _format;
        private string _contentEncoding;
        private string _pattern;
        private JsonNode _constant;
        private List<KeyValuePair<string, JsonSchema>> _properties;
        private List<string> _required;
        private JsonSchema _items;
        private JsonSchema _additionalProperties;
        private JsonArray _enum;
        private JsonSchema _not;
        private List<JsonSchema> _anyOf;
        private bool _hasDefaultValue;
        private JsonNode _defaultValue;
        private int? _minLength;
        private int? _maxLength;
        private bool? _deprecated;

        public string Ref { get => _ref; set { VerifyMutable(); _ref = value; } }
        public string Comment { get => _comment; set { VerifyMutable(); _comment = value; } }
        public JsonSchemaType Type { get => _type; set { VerifyMutable(); _type = value; } }
        public string Format { get => _format; set { VerifyMutable(); _format = value; } }
        public string ContentEncoding { get => _contentEncoding; set { VerifyMutable(); _contentEncoding = value; } }
        public string Pattern { get => _pattern; set { VerifyMutable(); _pattern = value; } }
        public JsonNode Constant { get => _constant; set { VerifyMutable(); _constant = value; } }
        public List<KeyValuePair<string, JsonSchema>> Properties { get => _properties; set { VerifyMutable(); _properties = value; } }
        public List<string> Required { get => _required; set { VerifyMutable(); _required = value; } }
        public JsonSchema Items { get => _items; set { VerifyMutable(); _items = value; } }
        public JsonSchema AdditionalProperties { get => _additionalProperties; set { VerifyMutable(); _additionalProperties = value; } }
        public JsonArray Enum { get => _enum; set { VerifyMutable(); _enum = value; } }
        public JsonSchema Not { get => _not; set { VerifyMutable(); _not = value; } }
        public List<JsonSchema> AnyOf { get => _anyOf; set { VerifyMutable(); _anyOf = value; } }
        public bool HasDefaultValue { get => _hasDefaultValue; set { VerifyMutable(); _hasDefaultValue = value; } }
        public JsonNode DefaultValue { get => _defaultValue; set { VerifyMutable(); _defaultValue = value; } }
        public int? MinLength { get => _minLength; set { VerifyMutable(); _minLength = value; } }
        public int? MaxLength { get => _maxLength; set { VerifyMutable(); _maxLength = value; } }
        public bool? Deprecated { get => _deprecated; set { VerifyMutable(); _deprecated = value; } }

        public JsonSchemaExporterContextImpl? ExporterContext { get; set; }

        public int KeywordCount
        {
            get
            {
                if (_trueOrFalse != null) return 0; // boolean schemas admit no keywords

                int count = 0;
                if (Ref != null) count++;
                if (Comment != null) count++;
                if (Type != JsonSchemaType.Any) count++;
                if (Format != null) count++;
                if (Pattern != null) count++;
                if (Constant != null) count++;
                if (Properties != null) count++;
                if (Required != null) count++;
                if (Items != null) count++;
                if (AdditionalProperties != null) count++;
                if (Enum != null) count++;
                if (Not != null) count++;
                if (AnyOf != null) count++;
                if (HasDefaultValue) count++;
                if (MinLength != null) count++;
                if (MaxLength != null) count++;
                if (Deprecated != null) count++;
                if (ContentEncoding != null) count++;
                return count;
            }
        }

        public void MakeNullable()
        {
            if (_trueOrFalse != null) return; // boolean schemas do not admit type keywords
            if (Type != JsonSchemaType.Any) Type |= JsonSchemaType.Null;
        }

        public JsonNode ToJsonNode(JsonSchemaExporterOptionsImpl options)
        {
            if (_trueOrFalse != null) return CompleteSchema((JsonNode)_trueOrFalse.Value, options);

            JsonObject objSchema = new JsonObject();

            if (Ref != null) objSchema.Add(RefPropertyName, (JsonNode)Ref);
            if (Comment != null) objSchema.Add(CommentPropertyName, (JsonNode)Comment);
            JsonNode type = MapSchemaType(Type);
            if (type != null) objSchema.Add(TypePropertyName, type);
            if (Format != null) objSchema.Add(FormatPropertyName, (JsonNode)Format);
            if (Pattern != null) objSchema.Add(PatternPropertyName, (JsonNode)Pattern);
            if (Constant != null) objSchema.Add(ConstPropertyName, Constant);

            if (Properties != null)
            {
                JsonObject properties = new JsonObject();
                foreach (KeyValuePair<string, JsonSchema> property in Properties)
                    properties.Add(property.Key, property.Value.ToJsonNode(options));
                objSchema.Add(PropertiesPropertyName, properties);
            }

            if (Required != null)
            {
                JsonArray requiredArray = new JsonArray();
                foreach (string requiredProperty in Required) requiredArray.Add((JsonNode)requiredProperty);
                objSchema.Add(RequiredPropertyName, requiredArray);
            }

            if (Items != null) objSchema.Add(ItemsPropertyName, Items.ToJsonNode(options));
            if (AdditionalProperties != null) objSchema.Add(AdditionalPropertiesPropertyName, AdditionalProperties.ToJsonNode(options));
            if (Enum != null) objSchema.Add(EnumPropertyName, Enum);
            if (Not != null) objSchema.Add(NotPropertyName, Not.ToJsonNode(options));

            if (AnyOf != null)
            {
                JsonArray anyOfArray = new JsonArray();
                foreach (JsonSchema schema in AnyOf) anyOfArray.Add(schema.ToJsonNode(options));
                objSchema.Add(AnyOfPropertyName, anyOfArray);
            }

            if (HasDefaultValue) objSchema.Add(DefaultPropertyName, DefaultValue);
            if (MinLength is int minLength) objSchema.Add(MinLengthPropertyName, (JsonNode)minLength);
            if (MaxLength is int maxLength) objSchema.Add(MaxLengthPropertyName, (JsonNode)maxLength);
            if (Deprecated is bool deprecated) objSchema.Add(DeprecatedPropertyName, (JsonNode)deprecated);
            if (ContentEncoding != null) objSchema.Add(ContentEncodingPropertyName, (JsonNode)ContentEncoding);

            return CompleteSchema(objSchema, options);
        }

        private JsonNode CompleteSchema(JsonNode schema, JsonSchemaExporterOptionsImpl options)
        {
            if (ExporterContext is JsonSchemaExporterContextImpl context)
                return options.TransformSchemaNode(context, schema); // user-defined transformations
            return schema;
        }

        /// <summary>Replaces a boolean schema with a semantically equivalent object schema that allows appending keywords.</summary>
        public static void EnsureMutable(ref JsonSchema schema)
        {
            if (schema._trueOrFalse == false) schema = new JsonSchema { Not = CreateTrueSchema() };
            else if (schema._trueOrFalse == true) schema = new JsonSchema();
        }

        // The order of these values influences the order of types in the rendered schema.
        private static readonly JsonSchemaType[] s_schemaValues =
        {
            JsonSchemaType.String,
            JsonSchemaType.Integer,
            JsonSchemaType.Number,
            JsonSchemaType.Boolean,
            JsonSchemaType.Array,
            JsonSchemaType.Object,
            JsonSchemaType.Null,
        };

        private void VerifyMutable()
        {
            if (_trueOrFalse != null) throw new InvalidOperationException();
        }

        public static JsonNode MapSchemaType(JsonSchemaType schemaType)
        {
            if (schemaType == JsonSchemaType.Any) return null;
            string identifier = ToIdentifier(schemaType);
            if (identifier != null) return (JsonNode)identifier;

            JsonArray array = new JsonArray();
            foreach (JsonSchemaType type in s_schemaValues)
                if ((schemaType & type) != 0) array.Add((JsonNode)ToIdentifier(type));
            return array;
        }

        private static string ToIdentifier(JsonSchemaType schemaType)
        {
            switch (schemaType)
            {
                case JsonSchemaType.Null: return "null";
                case JsonSchemaType.Boolean: return "boolean";
                case JsonSchemaType.Integer: return "integer";
                case JsonSchemaType.Number: return "number";
                case JsonSchemaType.String: return "string";
                case JsonSchemaType.Array: return "array";
                case JsonSchemaType.Object: return "object";
                default: return null;
            }
        }
    }

    [Shadows("System.Text.Json.Schema.JsonSchemaExporterOptions")]
    public sealed class JsonSchemaExporterOptionsImpl
    {
        private static JsonSchemaExporterOptionsImpl s_default;

        public static JsonSchemaExporterOptionsImpl Default => s_default ??= new JsonSchemaExporterOptionsImpl();

        public bool TreatNullObliviousAsNonNullable { get; init; }

        public Func<JsonSchemaExporterContextImpl, JsonNode, JsonNode> TransformSchemaNode { get; init; }
    }

    [Shadows("System.Text.Json.Schema.JsonSchemaExporterContext")]
    public readonly struct JsonSchemaExporterContextImpl
    {
        internal readonly string[] _path;

        internal JsonSchemaExporterContextImpl(JsonTypeInfoImpl typeInfo, JsonPropertyInfoImpl propertyInfo, JsonTypeInfoImpl baseTypeInfo, string[] path)
        {
            TypeInfo = typeInfo;
            PropertyInfo = propertyInfo;
            BaseTypeInfo = baseTypeInfo;
            _path = path;
        }

        public JsonTypeInfoImpl TypeInfo { get; }

        public JsonPropertyInfoImpl PropertyInfo { get; }

        public JsonTypeInfoImpl BaseTypeInfo { get; }

        public ReadOnlySpan<string> Path => _path;
    }

    [Shadows("System.Text.Json.Schema.JsonSchemaExporter")]
    public static class JsonSchemaExporterImpl
    {
        public static JsonNode GetJsonSchemaAsNode(this JsonSerializerOptionsImpl options, Type type, JsonSchemaExporterOptionsImpl exporterOptions = null)
        {
            if (options == null) throw new ArgumentNullException("options");
            if (type == null) throw new ArgumentNullException("type");

            ValidateOptions(options);
            JsonTypeInfoImpl typeInfo = options.GetTypeInfoInternal(type);
            if (typeInfo == null) options.ThrowNoMetadata(type);
            return GetJsonSchemaAsNode(typeInfo, exporterOptions);
        }

        public static JsonNode GetJsonSchemaAsNode(this JsonTypeInfoImpl typeInfo, JsonSchemaExporterOptionsImpl exporterOptions = null)
        {
            if (typeInfo == null) throw new ArgumentNullException("typeInfo");

            ValidateOptions(typeInfo.Options);
            exporterOptions ??= JsonSchemaExporterOptionsImpl.Default;

            typeInfo.EnsureConfigured();
            GenerationState state = new GenerationState(typeInfo.Options, exporterOptions);
            JsonSchema schema = MapJsonSchemaCore(state, typeInfo);
            return schema.ToJsonNode(exporterOptions);
        }

        private static JsonTypeInfoImpl Configured(JsonSerializerOptionsImpl options, Type type)
        {
            JsonTypeInfoImpl info = options.GetTypeInfoInternal(type);
            if (info == null) options.ThrowNoMetadata(type);
            info.EnsureConfigured();
            return info;
        }

        private static JsonTypeInfoImpl ElementTypeInfo(JsonTypeInfoImpl typeInfo)
        {
            JsonTypeInfoImpl info = typeInfo._elementTypeInfo ?? typeInfo._options.GetTypeInfoForRuntimeType(typeInfo._elementType);
            info.EnsureConfigured();
            return info;
        }

        private static JsonSchema MapJsonSchemaCore(
            GenerationState state,
            JsonTypeInfoImpl typeInfo,
            JsonPropertyInfoImpl propertyInfo = null,
            JsonConverterImpl customConverter = null,
            JsonNumberHandling? customNumberHandling = null,
            JsonTypeInfoImpl parentPolymorphicTypeInfo = null,
            bool parentPolymorphicTypeContainsTypesWithoutDiscriminator = false,
            bool parentPolymorphicTypeIsNonNullable = false,
            KeyValuePair<string, JsonSchema>? typeDiscriminator = null,
            bool cacheResult = true)
        {
            JsonSchemaExporterContextImpl exporterContext = state.CreateContext(typeInfo, propertyInfo, parentPolymorphicTypeInfo);

            if (cacheResult && typeInfo.Kind != JsonTypeInfoKind.None &&
                state.TryGetExistingJsonPointer(exporterContext, out string existingJsonPointer))
            {
                // The schema context has already been generated in the schema document, return a reference to it.
                return CompleteSchema(state, new JsonSchema { Ref = existingJsonPointer });
            }

            JsonConverterImpl effectiveConverter = customConverter ?? typeInfo.Converter;
            JsonNumberHandling effectiveNumberHandling = customNumberHandling ?? typeInfo.NumberHandling ?? typeInfo.Options.NumberHandling;
            JsonSchema schema = effectiveConverter.GetSchema(effectiveNumberHandling);
            if (schema != null)
            {
                // A schema has been provided by the converter.
                return CompleteSchema(state, schema);
            }

            if (parentPolymorphicTypeInfo == null && typeInfo.PolymorphismOptions != null && typeInfo.PolymorphismOptions.DerivedTypes.Count > 0)
            {
                // The base type of a polymorphic type hierarchy: an "anyOf" with the schemas of all derived types.
                JsonPolymorphismOptions polyOptions = typeInfo.PolymorphismOptions;
                string typeDiscriminatorKey = polyOptions.TypeDiscriminatorPropertyName;
                List<JsonDerivedType> derivedTypes = new List<JsonDerivedType>(polyOptions.DerivedTypes);

                if (!typeInfo.IsAbstractTypeInfo && !IsPolymorphicTypeThatSpecifiesItselfAsDerivedType(typeInfo))
                {
                    // Non-abstract base types that haven't been explicitly configured get a trivial schema, since they are supported.
                    derivedTypes.Add(new JsonDerivedType(typeInfo.Type));
                }

                bool containsTypesWithoutDiscriminator = derivedTypes.Exists(static derived => derived.TypeDiscriminator == null);
                JsonSchemaType schemaType = JsonSchemaType.Any;
                List<JsonSchema> anyOf = new List<JsonSchema>(derivedTypes.Count);

                state.PushSchemaNode(JsonSchema.AnyOfPropertyName);

                foreach (JsonDerivedType derivedType in derivedTypes)
                {
                    KeyValuePair<string, JsonSchema>? derivedTypeDiscriminator = null;
                    if (derivedType.TypeDiscriminator is object discriminatorValue)
                    {
                        JsonNode discriminatorNode = discriminatorValue is string stringId ? (JsonNode)stringId : (JsonNode)(int)discriminatorValue;
                        JsonSchema discriminatorSchema = new JsonSchema { Constant = discriminatorNode };
                        derivedTypeDiscriminator = new KeyValuePair<string, JsonSchema>(typeDiscriminatorKey, discriminatorSchema);
                    }

                    JsonTypeInfoImpl derivedTypeInfo = Configured(typeInfo.Options, derivedType.DerivedType);

                    state.PushSchemaNode(anyOf.Count.ToString(CultureInfo.InvariantCulture));
                    JsonSchema derivedSchema = MapJsonSchemaCore(
                        state,
                        derivedTypeInfo,
                        parentPolymorphicTypeInfo: typeInfo,
                        typeDiscriminator: derivedTypeDiscriminator,
                        parentPolymorphicTypeContainsTypesWithoutDiscriminator: containsTypesWithoutDiscriminator,
                        parentPolymorphicTypeIsNonNullable: propertyInfo != null && !propertyInfo.IsGetNullable && !propertyInfo.IsSetNullable,
                        cacheResult: false);

                    state.PopSchemaNode();

                    // Determine if all derived schemas have the same type.
                    if (anyOf.Count == 0) schemaType = derivedSchema.Type;
                    else if (schemaType != derivedSchema.Type) schemaType = JsonSchemaType.Any;

                    anyOf.Add(derivedSchema);
                }

                state.PopSchemaNode();

                if (schemaType != JsonSchemaType.Any)
                {
                    // All derived types share a schema type: move the type keyword to the base schema.
                    foreach (JsonSchema derivedSchema in anyOf)
                    {
                        derivedSchema.Type = JsonSchemaType.Any;

                        if (derivedSchema.KeywordCount == 0)
                        {
                            // Removing the type leaves an empty schema: the anyOf is always true.
                            anyOf = null;
                            break;
                        }
                    }
                }

                return CompleteSchema(state, new JsonSchema
                {
                    Type = schemaType,
                    AnyOf = anyOf,
                    // If all derived types have a discriminator, the base schema can require it.
                    Required = containsTypesWithoutDiscriminator ? null : new List<string> { typeDiscriminatorKey },
                });
            }

            JsonConverterImpl elementConverter = effectiveConverter.NullableElementConverter;
            if (elementConverter != null)
            {
                JsonTypeInfoImpl elementTypeInfo = Configured(typeInfo.Options, elementConverter.Type);
                schema = MapJsonSchemaCore(state, elementTypeInfo, customConverter: elementConverter, customNumberHandling: customNumberHandling ?? typeInfo.NumberHandling, cacheResult: false);

                if (elementConverter.IsIeeeFloatingPointConverter && (effectiveNumberHandling & JsonNumberHandling.AllowNamedFloatingPointLiterals) != 0)
                {
                    // IEEE floating-point types with AllowNamedFloatingPointLiterals generate an anyOf schema:
                    // fold null into the numeric branch to preserve nullability for nullable wrappers.
                    foreach (JsonSchema branch in schema.AnyOf)
                    {
                        if ((branch.Type & JsonSchemaType.Number) != 0)
                        {
                            branch.Type |= JsonSchemaType.Null;
                            break;
                        }
                    }
                }
                else if (schema.Enum != null)
                {
                    schema.Enum.Add((JsonNode)null); // append null to the enum array
                }

                return CompleteSchema(state, schema);
            }

            switch (typeInfo.Kind)
            {
                case JsonTypeInfoKind.Object:
                {
                    List<KeyValuePair<string, JsonSchema>> properties = null;
                    List<string> required = null;
                    JsonSchema additionalProperties = null;

                    JsonUnmappedMemberHandling effectiveUnmappedMemberHandling = typeInfo.UnmappedMemberHandling ?? typeInfo.Options.UnmappedMemberHandling;
                    if (effectiveUnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
                        additionalProperties = JsonSchema.CreateFalseSchema();

                    if (typeDiscriminator is KeyValuePair<string, JsonSchema> typeDiscriminatorPair)
                    {
                        (properties ??= new List<KeyValuePair<string, JsonSchema>>()).Add(typeDiscriminatorPair);
                        // Require the discriminator here when it isn't common to all derived types.
                        if (parentPolymorphicTypeContainsTypesWithoutDiscriminator) (required ??= new List<string>()).Add(typeDiscriminatorPair.Key);
                    }

                    state.PushSchemaNode(JsonSchema.PropertiesPropertyName);
                    foreach (JsonPropertyInfoImpl property in typeInfo._sortedProperties)
                    {
                        if ((property.Get == null && property.Set == null) || property.IsExtensionData)
                            continue; // skip JsonIgnored properties and extension data

                        state.PushSchemaNode(property.Name);
                        JsonSchema propertySchema = MapJsonSchemaCore(
                            state,
                            property.PropertyTypeInfoUntyped,
                            propertyInfo: property,
                            customConverter: property.EffectiveConverterUntyped,
                            customNumberHandling: EffectiveNumberHandling(property, typeInfo));

                        state.PopSchemaNode();

                        JsonParameterInfoValues parameter = property.AssociatedParameter;
                        if (parameter != null && parameter.HasDefaultValue)
                        {
                            JsonSchema.EnsureMutable(ref propertySchema);
                            propertySchema.DefaultValue = (JsonNode)(object)JsonSerializerImpl.SerializeToNodeUntyped(parameter.DefaultValue, property.PropertyTypeInfoUntyped);
                            propertySchema.HasDefaultValue = true;
                        }

                        (properties ??= new List<KeyValuePair<string, JsonSchema>>()).Add(new KeyValuePair<string, JsonSchema>(property.Name, propertySchema));

                        // Required if the property is, or the associated constructor parameter is non-optional. While the latter implies the
                        // former when RespectRequiredConstructorParameters is on, the exporter always marks non-optional parameters as required.
                        if (property.IsRequired || (parameter != null && !parameter.HasDefaultValue))
                            (required ??= new List<string>()).Add(property.Name);
                    }

                    state.PopSchemaNode();
                    return CompleteSchema(state, new JsonSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = properties,
                        Required = required,
                        AdditionalProperties = additionalProperties,
                    });
                }

                case JsonTypeInfoKind.Enumerable:
                {
                    JsonTypeInfoImpl elementInfo = ElementTypeInfo(typeInfo);

                    if (typeDiscriminator == null)
                    {
                        state.PushSchemaNode(JsonSchema.ItemsPropertyName);
                        JsonSchema items = MapJsonSchemaCore(state, elementInfo, customNumberHandling: effectiveNumberHandling);
                        state.PopSchemaNode();

                        return CompleteSchema(state, new JsonSchema
                        {
                            Type = JsonSchemaType.Array,
                            Items = items.IsTrue ? null : items,
                        });
                    }
                    else
                    {
                        // Polymorphic enumerable types are represented by a wrapping object:
                        // { "$type" : "discriminator", "$values" : [element1, element2, ...] }
                        const string ValuesKeyword = "$values";

                        state.PushSchemaNode(JsonSchema.PropertiesPropertyName);
                        state.PushSchemaNode(ValuesKeyword);
                        state.PushSchemaNode(JsonSchema.ItemsPropertyName);

                        JsonSchema items = MapJsonSchemaCore(state, elementInfo, customNumberHandling: effectiveNumberHandling);

                        state.PopSchemaNode();
                        state.PopSchemaNode();
                        state.PopSchemaNode();

                        return CompleteSchema(state, new JsonSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new List<KeyValuePair<string, JsonSchema>>
                            {
                                typeDiscriminator.Value,
                                new KeyValuePair<string, JsonSchema>(ValuesKeyword, new JsonSchema
                                {
                                    Type = JsonSchemaType.Array,
                                    Items = items.IsTrue ? null : items,
                                }),
                            },
                            Required = parentPolymorphicTypeContainsTypesWithoutDiscriminator ? new List<string> { typeDiscriminator.Value.Key } : null,
                        });
                    }
                }

                case JsonTypeInfoKind.Dictionary:
                {
                    JsonTypeInfoImpl elementInfo = ElementTypeInfo(typeInfo);

                    List<KeyValuePair<string, JsonSchema>> dictProps = null;
                    List<string> dictRequired = null;

                    if (typeDiscriminator is KeyValuePair<string, JsonSchema> dictDiscriminator)
                    {
                        dictProps = new List<KeyValuePair<string, JsonSchema>> { dictDiscriminator };
                        // Require the discriminator here when it isn't common to all derived types.
                        if (parentPolymorphicTypeContainsTypesWithoutDiscriminator) dictRequired = new List<string> { dictDiscriminator.Key };
                    }

                    state.PushSchemaNode(JsonSchema.AdditionalPropertiesPropertyName);
                    JsonSchema valueSchema = MapJsonSchemaCore(state, elementInfo, customNumberHandling: effectiveNumberHandling);
                    state.PopSchemaNode();

                    return CompleteSchema(state, new JsonSchema
                    {
                        Type = JsonSchemaType.Object,
                        Properties = dictProps,
                        Required = dictRequired,
                        AdditionalProperties = valueSchema.IsTrue ? null : valueSchema,
                    });
                }

                default:
                    // A `true` schema for types with user-defined converters.
                    return CompleteSchema(state, JsonSchema.CreateTrueSchema());
            }

            JsonSchema CompleteSchema(GenerationState generation, JsonSchema completed)
            {
                if (completed.Ref == null)
                {
                    if (IsNullableSchema(generation.ExporterOptions)) completed.MakeNullable();

                    bool IsNullableSchema(JsonSchemaExporterOptionsImpl options)
                    {
                        // Nullable when: a property whose getter or setter is nullable; a Nullable<T>; or a reference type,
                        // unless null-oblivious types are explicitly treated as non-nullable.
                        if (propertyInfo != null) return propertyInfo.IsGetNullable || propertyInfo.IsSetNullable;
                        if (typeInfo.IsNullable) return true;
                        return !typeInfo.IsValueTypeInfo && !parentPolymorphicTypeIsNonNullable && !options.TreatNullObliviousAsNonNullable;
                    }
                }

                if (HasObsoleteAttribute(typeInfo) || (propertyInfo != null && HasObsoleteAttribute(propertyInfo.AttributeProvider)))
                {
                    JsonSchema.EnsureMutable(ref completed);
                    completed.Deprecated = true;
                }

                if (generation.ExporterOptions.TransformSchemaNode != null)
                {
                    // Prime the schema for invocation by the JsonNode transformer.
                    completed.ExporterContext = exporterContext;
                }

                return completed;
            }
        }

        /// <summary>JsonPropertyInfo.EffectiveNumberHandling: the member, its declaring type, its type, then the options.</summary>
        private static JsonNumberHandling EffectiveNumberHandling(JsonPropertyInfoImpl property, JsonTypeInfoImpl declaring) =>
            property.NumberHandling ?? declaring.NumberHandling ?? property.PropertyTypeInfoUntyped.NumberHandling ?? property.Options.NumberHandling;

        private static void ValidateOptions(JsonSerializerOptionsImpl options)
        {
            if (options.ReferenceHandler == ReferenceHandlerImpl.Preserve)
                throw new NotSupportedException("JSON schema generation is not supported for contracts using ReferenceHandler.Preserve.");
            options.MakeReadOnly();
        }

        private static bool IsPolymorphicTypeThatSpecifiesItselfAsDerivedType(JsonTypeInfoImpl typeInfo)
        {
            foreach (JsonDerivedType derivedType in typeInfo.PolymorphismOptions.DerivedTypes)
                if (derivedType.DerivedType == typeInfo.Type) return true;
            return false;
        }

        // The real exporter matches ObsoleteAttribute by full name only because downlevel builds polyfill it; here it is the framework's.
        // A type's attribute is read at compile time: csharpc keeps attribute metadata only for types a program reflects over.
        private static bool HasObsoleteAttribute(JsonTypeInfoImpl typeInfo) => typeInfo.IsObsoleteTypeInfo;

        private static bool HasObsoleteAttribute(ICustomAttributeProvider attributeProvider) =>
            attributeProvider != null && attributeProvider.IsDefined(typeof(ObsoleteAttribute), true);

        private sealed class GenerationState
        {
            private readonly List<string> _currentPath = new List<string>();
            private readonly Dictionary<(JsonTypeInfoImpl, JsonPropertyInfoImpl), string[]> _generated = new Dictionary<(JsonTypeInfoImpl, JsonPropertyInfoImpl), string[]>();

            public GenerationState(JsonSerializerOptionsImpl options, JsonSchemaExporterOptionsImpl exporterOptions)
            {
                Options = options;
                ExporterOptions = exporterOptions;
            }

            public int CurrentDepth => _currentPath.Count;
            public JsonSerializerOptionsImpl Options { get; }
            public JsonSchemaExporterOptionsImpl ExporterOptions { get; }

            public void PushSchemaNode(string nodeId)
            {
                if (CurrentDepth == Options.EffectiveMaxDepth)
                    throw new InvalidOperationException("The depth of the generated JSON schema exceeds the JsonSerializerOptions.MaxDepth setting.");
                _currentPath.Add(nodeId);
            }

            public void PopSchemaNode() => _currentPath.RemoveAt(_currentPath.Count - 1);

            /// <summary>Registers the current schema node generation context; if it has already been generated, returns a JSON pointer to its location.</summary>
            public bool TryGetExistingJsonPointer(in JsonSchemaExporterContextImpl context, out string existingJsonPointer)
            {
                (JsonTypeInfoImpl, JsonPropertyInfoImpl) key = (context.TypeInfo, context.PropertyInfo);
                if (_generated.TryGetValue(key, out string[] pathToSchema))
                {
                    existingJsonPointer = FormatJsonPointer(pathToSchema);
                    return true;
                }

                _generated[key] = context._path;
                existingJsonPointer = null;
                return false;
            }

            public JsonSchemaExporterContextImpl CreateContext(JsonTypeInfoImpl typeInfo, JsonPropertyInfoImpl propertyInfo, JsonTypeInfoImpl baseTypeInfo) =>
                new JsonSchemaExporterContextImpl(typeInfo, propertyInfo, baseTypeInfo, _currentPath.ToArray());

            private static string FormatJsonPointer(string[] path)
            {
                if (path.Length == 0) return "#";

                StringBuilder sb = new StringBuilder("#");
                foreach (string segment in path)
                {
                    sb.Append('/');
                    // RFC 6901: '~' and '/' are escaped as '~0' and '~1'; and since the pointer is embedded in a URI
                    // fragment (section 6), characters not valid there are percent-encoded.
                    sb.Append(Uri.EscapeDataString(segment.Replace("~", "~0").Replace("/", "~1")));
                }
                return sb.ToString();
            }
        }
    }
}
