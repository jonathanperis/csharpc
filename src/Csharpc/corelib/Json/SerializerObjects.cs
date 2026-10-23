// System.Text.Json object, collection and dictionary converters over source-generated metadata.
//
// Ported from dotnet/runtime src/libraries/System.Text.Json/src/System/Text/Json/Serialization/Converters
// (release/11.0): ObjectDefaultConverter, ObjectWithParameterizedConstructorConverter, the collection
// and dictionary converters, polymorphism (PolymorphicTypeResolver) and extension data. Reading is
// recursive (the payload is always complete), with .NET's exception messages and JSON paths.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CsharpcCoreLib
{
    internal sealed class ObjectConverter<T> : JsonConverterImpl<T>, IObjectWriter
    {
        // The work is in the non-generic ObjectConverterCore: only the typed entry points are instantiated per T,
        // which keeps generated code (and compile time) from growing with every serializable type.
        internal JsonTypeInfoImpl<T> TypeInfo;
        internal bool NoConstructor;

        void IObjectWriter.WriteWithDiscriminator(Utf8JsonWriterImpl writer, object obj, JsonWriteState state, JsonPolymorphicTypeResolver poly, object discriminator) =>
            ObjectConverterCore.WriteObject(TypeInfo, writer, obj, TypeInfo._options, state, poly, discriminator);

        object IObjectWriter.ReadSkippingDiscriminator(ref Utf8JsonReaderImpl reader, JsonReadState state, string discriminatorName) =>
            ObjectConverterCore.ReadObject(TypeInfo, ref reader, TypeInfo._options, state, discriminatorName, null);

        internal override bool IsInternalConverter => true;

        internal override JsonTypeInfoKind Kind => JsonTypeInfoKind.Object;

        public override T Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            ReadWithState(ref reader, typeToConvert, options, new JsonReadState(options));

        public override void Write(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options) =>
            WriteWithState(writer, value, options, new JsonWriteState(options));

        internal override void WriteWithState(Utf8JsonWriterImpl writer, T value, JsonSerializerOptionsImpl options, JsonWriteState state) =>
            ObjectConverterCore.WriteValue(TypeInfo, writer, value, options, state);

        internal override T ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            object result = ObjectConverterCore.ReadObject(TypeInfo, ref reader, options, state, null, null);
            return result == null ? default(T) : (T)result;
        }

        internal override bool CanPopulate => true;

        internal override T ReadInto(ref Utf8JsonReaderImpl reader, T existing, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            object result = ObjectConverterCore.ReadObject(TypeInfo, ref reader, options, state, null, existing);
            return result == null ? default(T) : (T)result;
        }
    }

    /// <summary>ObjectDefaultConverter / ObjectWithParameterizedConstructorConverter over untyped metadata.</summary>
    internal static class ObjectConverterCore
    {
        public static void WriteValue(JsonTypeInfoImpl info, Utf8JsonWriterImpl writer, object value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (writer.CurrentDepth >= options.EffectiveMaxDepth)
                throw new JsonExceptionImpl(JsonSerializerHelpers.CycleDetected(options.EffectiveMaxDepth)) { AppendPathInformation = true };
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            info.EnsureConfigured();
            object obj = value;
            if (info.SerializationOnly)
            {
                info.ThrowIfFastPathUnusable();
                info._untypedSerializeHandler((Utf8JsonWriter)(object)writer, obj);
                return;
            }
            JsonPolymorphicTypeResolver poly = info._polymorphicResolver;
            if (poly != null)
            {
                Type runtimeType = obj.GetType();
                if (poly.TryGetDerivedType(runtimeType, out Type derived, out object discriminator))
                {
                    if (runtimeType != info.Type)
                    {
                        JsonTypeInfoImpl derivedInfo = options.GetTypeInfoForRuntimeType(runtimeType);
                        JsonObjectWriting.WriteDerived(writer, obj, derivedInfo, poly, discriminator, state);
                        return;
                    }
                    WriteObject(info, writer, obj, options, state, poly, discriminator);
                    return;
                }
                if (runtimeType != info.Type)
                {
                    switch (poly.UnknownHandling)
                    {
                        case JsonUnknownDerivedTypeHandling.FailSerialization:
                            throw new NotSupportedException("Runtime type '" + runtimeType.ToString() + "' is not supported by polymorphic type '" + info.Type.ToString() + "'.");
                        case JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor:
                            if (poly.TryGetNearestAncestor(runtimeType, info.Type, out Type ancestor, out object ancestorDiscriminator))
                            {
                                JsonTypeInfoImpl ancestorInfo = options.GetTypeInfoForRuntimeType(ancestor);
                                JsonObjectWriting.WriteDerived(writer, obj, ancestorInfo, poly, ancestorDiscriminator, state);
                                return;
                            }
                            break;
                    }
                }
            }
            WriteObject(info, writer, obj, options, state, null, null);
        }

        public static void WriteObject(JsonTypeInfoImpl info, Utf8JsonWriterImpl writer, object obj, JsonSerializerOptionsImpl options, JsonWriteState state,
            JsonPolymorphicTypeResolver discriminatorSource, object discriminator)
        {
            info.EnsureConfigured();
            if (obj is IJsonOnSerializing serializing) serializing.OnSerializing();
            info.OnSerializing?.Invoke(obj);

            if (state.ReferenceResolver != null && !info.IsValueTypeInfo)
            {
                string id = state.ReferenceResolver.GetReference(obj, out bool alreadyExists);
                writer.WriteStartObject();
                if (alreadyExists)
                {
                    writer.WriteString("$ref", id);
                    writer.WriteEndObject();
                    return;
                }
                writer.WriteString("$id", id);
            }
            else if (state.Ancestors != null && !info.IsValueTypeInfo)
            {
                foreach (object ancestor in state.Ancestors)
                {
                    if (ReferenceEquals(ancestor, obj))
                    {
                        writer.WriteNullValue();
                        return;
                    }
                }
                writer.WriteStartObject();
            }
            else writer.WriteStartObject();

            if (discriminatorSource != null && discriminator != null)
            {
                writer.WritePropertyNameUtf8Escaped(discriminatorSource.EscapedDiscriminatorName);
                if (discriminator is string s) writer.WriteStringValue(s);
                else writer.WriteNumberValue((int)discriminator);
            }

            if (state.Ancestors != null && !info.IsValueTypeInfo) state.Ancestors.Add(obj);
            JsonPropertyInfoImpl[] properties = info._sortedProperties;
            for (int i = 0; i < properties.Length; i++)
            {
                JsonPropertyInfoImpl property = properties[i];
                if (property.CanSerialize) property.WriteMember(writer, obj, state);
            }
            if (info._extensionData != null && info._extensionData.CanSerialize)
                JsonObjectWriting.WriteExtensionData(writer, info._extensionData.GetValueAsObject(obj), options, state);
            if (state.Ancestors != null && !info.IsValueTypeInfo) state.Ancestors.RemoveAt(state.Ancestors.Count - 1);

            writer.WriteEndObject();
            info.OnSerialized?.Invoke(obj);
            if (obj is IJsonOnSerialized serialized) serialized.OnSerialized();
        }

        // ------------------------------------------------------------------ reading

        public static object ReadObject(JsonTypeInfoImpl info, ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options, JsonReadState state, string skipDiscriminator, object existing)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (!info.IsNullableOrReference) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? info.Type);
                return null;
            }
            if (reader.TokenType != JsonTokenType.StartObject) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? info.Type);
            info.EnsureConfigured();

            JsonPolymorphicTypeResolver poly = info._polymorphicResolver;
            if (skipDiscriminator == null && ((poly != null && poly.UsesDiscriminators) || state.References != null))
            {
                // Nothing to instantiate without a discriminator: .NET has read the metadata properties by the time it finds out.
                bool cannotCreate = !info.HasCreateObject && !info.HasParameterizedCreator;
                JsonObjectMetadata metadata = JsonObjectReading.ScanMetadata(ref reader, poly != null && poly.UsesDiscriminators ? poly : null,
                    options, state, info.IsValueTypeInfo, info.Type, cannotCreate);
                if (metadata.Ref != null) return JsonObjectReading.ResolveReference(metadata.Ref, state);
                if (metadata.DerivedType != null) return JsonObjectReading.ReadDerived(ref reader, info, poly, metadata.DerivedType, options, state);
            }
            string metadataDiscriminator = skipDiscriminator ?? (poly != null && poly.UsesDiscriminators ? poly.DiscriminatorName : null);

            if (info.HasParameterizedConstructor) return ReadWithParameterizedConstructor(info, ref reader, options, state, skipDiscriminator, metadataDiscriminator);

            bool create = info.HasCreateObject;
            // No parameterless creator but not parameterized either: required/init-only members read the reflection way.
            bool viaMemberInitializers = !create && info.HasParameterizedCreator;
            if (!create && !viaMemberInitializers && existing == null) throw NoConstructorException(info);
            object obj = existing ?? (create ? info.CreateObjectUntyped() : info.CreateParameterizedUntyped(info.DefaultConstructorArguments()));
            if (obj is IJsonOnDeserializing deserializing) deserializing.OnDeserializing();
            info.OnDeserializing?.Invoke(obj);

            bool[] requiredSeen = info._requiredProperties == null ? null : new bool[info._requiredProperties.Length];
            HashSet<string> seen = options.AllowDuplicateProperties ? null : new HashSet<string>();
            bool leadingMetadata = true;
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndObject) break;
                string name = reader.GetString();
                if (skipDiscriminator != null && name == skipDiscriminator)
                {
                    // The discriminator ScanMetadata resolved: only the first one is metadata.
                    skipDiscriminator = null;
                    reader.Read();
                    reader.Skip();
                    continue;
                }
                if (state.References != null && leadingMetadata && name == "$id")
                {
                    // Validated by ScanMetadata; registered here, once the instance exists.
                    reader.Read();
                    JsonObjectReading.RegisterId(reader.GetString(), obj, state);
                    continue;
                }
                if (JsonObjectReading.SkipOrRejectMetadata(ref reader, name, metadataDiscriminator, options, state)) continue;
                leadingMetadata = false;
                // The property is on the path before its value is read, so reader errors in it report "$.name".
                state.PushProperty(name);
                reader.Read();
                JsonPropertyInfoImpl property = info.GetProperty(name);
                if (seen != null && property != null && !seen.Add(property.Name))
                    throw new JsonExceptionImpl("Duplicate property '" + property.Name + "' encountered during deserialization of type '" + property.DeclaringType.ToString() + "'.");
                if (property == null)
                {
                    JsonObjectReading.ReadUnmapped(ref reader, info, obj, name, options, state);
                }
                else if (!property.CanDeserialize) reader.Skip();
                else
                {
                    property.ReadAndSetMember(ref reader, obj, state);
                    if (requiredSeen != null) MarkRequired(info, requiredSeen, property);
                }
                state.Pop();
            }
            if (requiredSeen != null) JsonObjectReading.CheckRequired(info, requiredSeen);
            info.OnDeserialized?.Invoke(obj);
            if (obj is IJsonOnDeserialized deserialized) deserialized.OnDeserialized();
            return obj;
        }

        private static void MarkRequired(JsonTypeInfoImpl info, bool[] seen, JsonPropertyInfoImpl property)
        {
            JsonPropertyInfoImpl[] required = info._requiredProperties;
            for (int i = 0; i < required.Length; i++)
                if (ReferenceEquals(required[i], property)) seen[i] = true;
        }

        private static NotSupportedException NoConstructorException(JsonTypeInfoImpl info)
        {
            if (info._polymorphicResolver != null && info._polymorphicResolver.UsesDiscriminators)
                return new NotSupportedException("The JSON payload for polymorphic interface or abstract type '" + info.Type.ToString() + "' must specify a type discriminator.");
            if (info.IsAbstractTypeInfo)
                return new NotSupportedException("Deserialization of interface or abstract types is not supported. Type '" + info.Type.ToString() + "'.");
            return new NotSupportedException("Deserialization of types without a parameterless constructor, a singular parameterized constructor, or a parameterized constructor annotated with 'JsonConstructorAttribute' is not supported. Type '" + info.Type.ToString() + "'.");
        }

        private static object ReadWithParameterizedConstructor(JsonTypeInfoImpl info, ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options, JsonReadState state, string skipDiscriminator, string metadataDiscriminator)
        {
            JsonParameterInfoValues[] parameters = info._parameters;
            int count = info._parameterCount;
            object[] args = new object[count];
            bool[] argSet = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if (info._parameterProperties[i] == null)
                    throw new InvalidOperationException("Each parameter in the deserialization constructor on type '" + info.Type.ToString() + "' must bind to an object property or field on deserialization. Each parameter name and type must match with a property or field on the object. Fields are only considered when 'JsonSerializerOptions.IncludeFields' is enabled. The name match can be case-insensitive.");
            }
            args = info.DefaultConstructorArguments();
            List<JsonPropertyInfoImpl> pendingProperties = null;
            List<object> pendingValues = null;
            bool[] requiredSeen = info._requiredProperties == null ? null : new bool[info._requiredProperties.Length];
            HashSet<string> seen = options.AllowDuplicateProperties ? null : new HashSet<string>();
            List<KeyValuePair<string, JsonElement>> pendingExtension = null;
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndObject) break;
                string name = reader.GetString();
                if (skipDiscriminator != null && name == skipDiscriminator)
                {
                    skipDiscriminator = null;
                    reader.Read();
                    reader.Skip();
                    continue;
                }
                if (JsonObjectReading.SkipOrRejectMetadata(ref reader, name, metadataDiscriminator, options, state)) continue;
                state.PushProperty(name);
                reader.Read();
                JsonPropertyInfoImpl property = info.GetProperty(name);
                if (seen != null && property != null && !seen.Add(property.Name))
                    throw new JsonExceptionImpl("Duplicate property '" + property.Name + "' encountered during deserialization of type '" + property.DeclaringType.ToString() + "'.");
                if (property == null)
                {
                    if (info._extensionData != null)
                    {
                        pendingExtension ??= new List<KeyValuePair<string, JsonElement>>();
                        pendingExtension.Add(new KeyValuePair<string, JsonElement>(name, (JsonElement)(object)JsonElementImpl.ParseValue(ref reader)));
                    }
                    else JsonObjectReading.ReadUnmapped(ref reader, info, null, name, options, state);
                }
                else if (property.AssociatedParameterPosition >= 0)
                {
                    int position = property.AssociatedParameterPosition;
                    if (reader.TokenType == JsonTokenType.Null && property.IgnoreNullTokensOnRead) { }
                    else
                    {
                        args[position] = property.ReadValueAsObject(ref reader, state);
                        argSet[position] = true;
                    }
                    if (requiredSeen != null) MarkRequired(info, requiredSeen, property);
                }
                else if (!property.CanDeserialize) reader.Skip();
                else
                {
                    if (!(reader.TokenType == JsonTokenType.Null && property.IgnoreNullTokensOnRead))
                    {
                        pendingProperties ??= new List<JsonPropertyInfoImpl>();
                        pendingValues ??= new List<object>();
                        pendingProperties.Add(property);
                        pendingValues.Add(property.ReadValueAsObject(ref reader, state));
                    }
                    if (requiredSeen != null) MarkRequired(info, requiredSeen, property);
                }
                state.Pop();
            }
            if (options.RespectRequiredConstructorParameters)
            {
                for (int i = 0; i < count; i++)
                {
                    JsonParameterInfoValues p = parameters[i];
                    if (!argSet[p.Position] && !p.HasDefaultValue && !p.IsMemberInitializer)
                        throw new JsonExceptionImpl("JSON deserialization for type '" + info.Type.ToString() + "' was missing required properties including: '" + info._parameterProperties[p.Position].Name + "'.");
                }
            }
            if (requiredSeen != null) JsonObjectReading.CheckRequired(info, requiredSeen);
            object obj = info.CreateParameterizedUntyped(args);
            if (pendingProperties != null)
                for (int i = 0; i < pendingProperties.Count; i++) pendingProperties[i].SetValueAsObject(obj, pendingValues[i]);
            if (pendingExtension != null)
                foreach (KeyValuePair<string, JsonElement> kv in pendingExtension) JsonObjectReading.AddExtensionValue(info, obj, kv.Key, kv.Value, options);
            info.OnDeserialized?.Invoke(obj);
            if (obj is IJsonOnDeserialized deserialized) deserialized.OnDeserialized();
            return obj;
        }
        }

    internal static class JsonObjectWriting
    {
        public static void WriteDerived(Utf8JsonWriterImpl writer, object obj, JsonTypeInfoImpl derivedInfo, JsonPolymorphicTypeResolver poly, object discriminator, JsonWriteState state)
        {
            derivedInfo.EnsureConfigured();
            if (derivedInfo._converter is IObjectWriter objectWriter) objectWriter.WriteWithDiscriminator(writer, obj, state, poly, discriminator);
            else derivedInfo.WriteAsObject(writer, obj, state);
        }

        public static void WriteExtensionData(Utf8JsonWriterImpl writer, object extension, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (extension == null) return;
            if (extension is IDictionary<string, JsonElement> elements)
            {
                foreach (KeyValuePair<string, JsonElement> kv in elements)
                {
                    writer.WritePropertyName(kv.Key);
                    ((JsonElementImpl)(object)kv.Value).WriteTo(writer);
                }
            }
            else if (extension is IDictionary<string, object> objects)
            {
                JsonConverterImpl<object> objectConverter = (JsonConverterImpl<object>)(object)JsonMetadataServicesImpl.ObjectConverter;
                foreach (KeyValuePair<string, object> kv in objects)
                {
                    writer.WritePropertyName(kv.Key);
                    state.Push(kv.Key);
                    objectConverter.WriteWithState(writer, kv.Value, options, state);
                    state.Pop();
                }
            }
            else if (extension is JsonObject node)
            {
                foreach (KeyValuePair<string, JsonNode> kv in node)
                {
                    writer.WritePropertyName(kv.Key);
                    if (kv.Value == null) writer.WriteNullValue();
                    else ((JsonNodeImpl)(object)kv.Value).WriteTo(writer, options);
                }
            }
        }
    }

    internal struct JsonObjectMetadata
    {
        public string Id;
        public string Ref;
        public Type DerivedType;
    }

    internal interface IObjectWriter
    {
        void WriteWithDiscriminator(Utf8JsonWriterImpl writer, object obj, JsonWriteState state, JsonPolymorphicTypeResolver poly, object discriminator);
        object ReadSkippingDiscriminator(ref Utf8JsonReaderImpl reader, JsonReadState state, string discriminatorName);
    }

    internal static class JsonObjectReading
    {
        public static bool IsMetadataName(string name) => name == "$id" || name == "$ref" || name == "$values";

        public static void RegisterId(string id, object obj, JsonReadState state)
        {
            if (state.References.ContainsKey(id))
                throw new JsonExceptionImpl("The value of the '$id' metadata property '" + id + "' conflicts with an existing identifier.") { AppendPathInformation = true };
            state.References[id] = obj;
        }

        public static object ResolveReference(string id, JsonReadState state)
        {
            if (!state.References.TryGetValue(id, out object target))
                throw new JsonExceptionImpl("Reference '" + id + "' was not found.") { AppendPathInformation = true };
            return target;
        }

        public static void ReadUnmapped(ref Utf8JsonReaderImpl reader, JsonTypeInfoImpl info, object obj, string name, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            if (info._extensionData != null && obj != null)
            {
                JsonElement element = (JsonElement)(object)JsonElementImpl.ParseValue(ref reader);
                AddExtensionValue(info, obj, name, element, options);
                return;
            }
            JsonUnmappedMemberHandling handling = info.UnmappedMemberHandling ?? options.UnmappedMemberHandling;
            if (handling == JsonUnmappedMemberHandling.Disallow)
                throw new JsonExceptionImpl("The JSON property '" + name + "' could not be mapped to any .NET member contained in type '" + info.Type.ToString() + "'.");
            reader.Skip();
        }

        public static void AddExtensionValue(JsonTypeInfoImpl info, object obj, string name, JsonElement element, JsonSerializerOptionsImpl options)
        {
            JsonPropertyInfoImpl extension = info._extensionData;
            object dictionary = extension.GetValueAsObject(obj);
            if (dictionary == null)
            {
                if (extension.UntypedSetter == null)
                    throw new NotSupportedException("csharpc: the extension data property '" + extension.Name + "' is null and has no setter.");
                if (extension.PropertyType == typeof(JsonObject)) dictionary = new JsonObject();
                else if (extension.PropertyType == typeof(Dictionary<string, object>) || extension.PropertyType == typeof(IDictionary<string, object>)) dictionary = new Dictionary<string, object>();
                else dictionary = new Dictionary<string, JsonElement>();
                extension.SetValueAsObject(obj, dictionary);
            }
            if (dictionary is IDictionary<string, JsonElement> elements)
            {
                if (!options.AllowDuplicateProperties && elements.ContainsKey(name))
                    throw new JsonExceptionImpl("Duplicate property '" + name + "' encountered during deserialization.");
                elements[name] = element;
            }
            else if (dictionary is IDictionary<string, object> objects)
            {
                JsonElementImpl e = (JsonElementImpl)(object)element;
                objects[name] = e.ValueKind == JsonValueKind.Null ? null : (object)element;
            }
            else if (dictionary is JsonObject node)
            {
                node[name] = (JsonNode)(object)JsonNodeImpl.CreateNode((JsonElementImpl)(object)element, null);
            }
        }

        public static void CheckRequired(JsonTypeInfoImpl info, bool[] seen)
        {
            StringBuilder missing = null;
            for (int i = 0; i < seen.Length; i++)
            {
                if (seen[i]) continue;
                if (missing == null) missing = new StringBuilder();
                else missing.Append(", ");
                missing.Append('\'').Append(info._requiredProperties[i].Name).Append('\'');
            }
            if (missing != null)
                throw new JsonExceptionImpl("JSON deserialization for type '" + info.Type.ToString() + "' was missing required properties including: " + missing.ToString() + ".");
        }

        /// <summary>Reads the type discriminator (first property) and deserializes the derived type it names.</summary>
        /// <summary>
        /// A '$'-prefixed (or custom discriminator) property where a type that can contain metadata expects a
        /// regular one: skipped with AllowOutOfOrderMetadataProperties, else the errors of GetPropertyName.
        /// </summary>
        public static bool SkipOrRejectMetadata(ref Utf8JsonReaderImpl reader, string name, string discriminatorName, JsonSerializerOptionsImpl options, JsonReadState state)
        {
            if (discriminatorName == null && state.References == null) return false;
            if (name != discriminatorName && (name.Length == 0 || name[0] != '$')) return false;
            if (options.AllowOutOfOrderMetadataProperties)
            {
                reader.Read();
                reader.Skip();
                return true;
            }
            state.PushProperty(name);
            bool known = name == "$id" || name == "$ref" || name == "$values" || name == discriminatorName || (name == "$type" && discriminatorName == null);
            throw new JsonExceptionImpl(known
                ? "The metadata property is either not supported by the type or is not the first property in the deserialized JSON object."
                : "Properties that start with '$' are not allowed in types that support metadata. Either replace the character or disable reference preservation and polymorphic deserialization.") { AppendPathInformation = true };
        }

        private static JsonExceptionImpl MetadataError(string message) => new JsonExceptionImpl(message) { AppendPathInformation = true };

        private const string MetadataUnexpectedProperty = "The metadata property is either not supported by the type or is not the first property in the deserialized JSON object.";

        /// <summary>
        /// JsonSerializer.TryReadMetadata on a copy of the reader: validates the leading metadata properties
        /// ($id, $ref, the type discriminator; all of them with AllowOutOfOrderMetadataProperties) with .NET's
        /// errors and positions, and resolves the discriminator. The reader stays at the start of the object
        /// (the object loop re-reads $id once the instance exists), except after a $ref (left at its end) or
        /// when <paramref name="advanceIfUnresolved"/> and there's no discriminator.
        /// </summary>
        public static JsonObjectMetadata ScanMetadata(ref Utf8JsonReaderImpl reader, JsonPolymorphicTypeResolver poly, JsonSerializerOptionsImpl options,
            JsonReadState state, bool isValueType, Type type, bool advanceIfUnresolved)
        {
            JsonObjectMetadata result = default(JsonObjectMetadata);
            bool outOfOrder = options.AllowOutOfOrderMetadataProperties;
            bool references = state.References != null;
            bool any = false;
            bool sawRegularProperty = false;
            object discriminator = null;
            string discriminatorName = poly?.DiscriminatorName;
            Utf8JsonReaderImpl probe = reader;
            while (true)
            {
                probe.Read();
                if (probe.TokenType == JsonTokenType.EndObject) break;
                string name = probe.GetString();
                if (result.Ref != null)
                {
                    state.PushProperty(name);
                    reader = probe;
                    throw MetadataError("A JSON object that contains a '$ref' metadata property must not contain any other properties.");
                }
                // GetMetadataPropertyName: "$type" only when no custom discriminator name is configured.
                int kind = name == "$id" ? 1 : name == "$ref" ? 2 : name == discriminatorName || (name == "$type" && (discriminatorName == null || discriminatorName == "$type")) ? 3 : 0;
                if (kind == 0)
                {
                    if (!outOfOrder) break;
                    sawRegularProperty = true;
                    if (name.Length > 0 && name[0] == '$')
                    {
                        state.PushProperty(name);
                        reader = probe;
                        throw MetadataError(name == "$values" ? MetadataUnexpectedProperty : "Properties that start with '$' are not allowed in types that support metadata. Either replace the character or disable reference preservation and polymorphic deserialization.");
                    }
                    probe.Read();
                    probe.Skip();
                    continue;
                }
                state.PushProperty(name);
                if (kind == 1)
                {
                    if (!references) { reader = probe; throw MetadataError(MetadataUnexpectedProperty); }
                    if (result.Id != null || result.Ref != null) { reader = probe; throw MetadataError("The metadata property '$id' cannot be used together with '$ref' metadata properties."); }
                }
                else if (kind == 2)
                {
                    if (!references) { reader = probe; throw MetadataError(MetadataUnexpectedProperty); }
                    if (isValueType) { reader = probe; throw MetadataError("Invalid reference to value type '" + type.ToString() + "'."); }
                    if (any || sawRegularProperty) { reader = probe; throw MetadataError("A JSON object that contains a '$ref' metadata property must not contain any other properties."); }
                }
                else
                {
                    if (poly == null) { reader = probe; throw MetadataError(MetadataUnexpectedProperty); }
                    if (discriminator != null) { reader = probe; throw MetadataError("Deserialized object contains a duplicate '" + name + "' metadata property."); }
                }
                probe.Read();
                if (kind == 3 && probe.TokenType == JsonTokenType.Number)
                {
                    if (!probe.TryGetInt32(out int number))
                    {
                        reader = probe;
                        probe.GetInt32();
                    }
                    discriminator = number;
                }
                else if (probe.TokenType != JsonTokenType.String)
                {
                    reader = probe;
                    throw MetadataError("The '$id', '$ref' or '$type' metadata properties must be JSON strings. Current token type is '" + probe.TokenType.ToString() + "'.");
                }
                else if (kind == 1) result.Id = probe.GetString();
                else if (kind == 2) result.Ref = probe.GetString();
                else discriminator = probe.GetString();
                state.Pop();
                any = true;
            }
            if (result.Ref != null)
            {
                reader = probe;
                return result;
            }
            if (discriminator == null)
            {
                if (advanceIfUnresolved) reader = probe;
                return result;
            }
            Type derived;
            if (!(discriminator is string text ? poly.TryGetTypeByDiscriminator(text, out derived) : poly.TryGetTypeByDiscriminator((int)discriminator, out derived)))
            {
                if (poly.IgnoreUnrecognized)
                {
                    if (advanceIfUnresolved) reader = probe;
                    return result;
                }
                reader = probe;
                throw MetadataError("Read unrecognized type discriminator id '" + (discriminator is string ? (string)discriminator : ((int)discriminator).ToString()) + "'.");
            }
            result.DerivedType = derived;
            return result;
        }

        /// <summary>Reads the object at the reader as the derived type a discriminator selected.</summary>
        public static object ReadDerived(ref Utf8JsonReaderImpl reader, JsonTypeInfoImpl info, JsonPolymorphicTypeResolver poly, Type derived,
            JsonSerializerOptionsImpl options, JsonReadState state)
        {
            if (derived == info.Type)
            {
                if (info._converter is IObjectWriter self) return self.ReadSkippingDiscriminator(ref reader, state, poly.DiscriminatorName);
            }
            JsonTypeInfoImpl derivedInfo = options.GetTypeInfoForRuntimeType(derived);
            if (derivedInfo._converter is IObjectWriter objectReader) return objectReader.ReadSkippingDiscriminator(ref reader, state, poly.DiscriminatorName);
            return derivedInfo.ReadAsObject(ref reader, state);
        }
    }

    // ------------------------------------------------------------------------------------------
    // Collections
    // ------------------------------------------------------------------------------------------

    internal abstract class CollectionConverter<TCollection, TElement> : JsonConverterImpl<TCollection>
    {
        internal JsonTypeInfoImpl<TCollection> TypeInfo;
        internal Func<TCollection> Create;
        private JsonConverterImpl<TElement> _element;

        internal override bool IsInternalConverter => true;

        internal override JsonTypeInfoKind Kind => JsonTypeInfoKind.Enumerable;

        internal override bool IsNumberHandlingApplicable => Element(TypeInfo._options).IsNumberHandlingApplicable;

        internal JsonConverterImpl<TElement> Element(JsonSerializerOptionsImpl options)
        {
            if (_element == null)
            {
                JsonTypeInfoImpl<TElement> info = (JsonTypeInfoImpl<TElement>)(TypeInfo._elementTypeInfo ?? options.GetTypeInfoForRuntimeType(typeof(TElement)));
                info.EnsureConfigured();
                _element = info.TypedConverter;
            }
            return _element;
        }

        public override TCollection Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            ReadWithState(ref reader, typeToConvert, options, new JsonReadState(options));

        public override void Write(Utf8JsonWriterImpl writer, TCollection value, JsonSerializerOptionsImpl options) =>
            WriteWithState(writer, value, options, new JsonWriteState(options));

        protected abstract IEnumerable<TElement> Elements(TCollection collection);

        /// <summary>A new collection for the elements read (in JSON order).</summary>
        protected abstract TCollection FromElements(List<TElement> elements, JsonReadState state);

        /// <summary>Appends the elements read to an existing collection (JsonObjectCreationHandling.Populate).</summary>
        protected virtual TCollection AddElements(TCollection existing, List<TElement> elements) => throw new InvalidOperationException("csharpc: collection cannot populate");

        internal override TCollection ReadInto(ref Utf8JsonReaderImpl reader, TCollection existing, JsonSerializerOptionsImpl options, JsonReadState state) =>
            ReadCollection(ref reader, options, state, existing, true);

        internal override void WriteWithState(Utf8JsonWriterImpl writer, TCollection value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (writer.CurrentDepth >= options.EffectiveMaxDepth)
                throw new JsonExceptionImpl(JsonSerializerHelpers.CycleDetected(options.EffectiveMaxDepth)) { AppendPathInformation = true };
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            JsonConverterImpl<TElement> element = Element(options);
            if (state.ReferenceResolver != null && !CoreIntrinsics.IsValueType<TCollection>())
            {
                string id = state.ReferenceResolver.GetReference(value, out bool alreadyExists);
                writer.WriteStartObject();
                if (alreadyExists)
                {
                    writer.WriteString("$ref", id);
                    writer.WriteEndObject();
                    return;
                }
                writer.WriteString("$id", id);
                writer.WritePropertyName("$values");
                WriteElements(writer, value, element, options, state);
                writer.WriteEndObject();
                return;
            }
            WriteElements(writer, value, element, options, state);
        }

        private void WriteElements(Utf8JsonWriterImpl writer, TCollection value, JsonConverterImpl<TElement> element, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            writer.WriteStartArray();
            foreach (TElement item in Elements(value)) element.WriteWithState(writer, item, options, state);
            writer.WriteEndArray();
        }

        internal override TCollection ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state) =>
            ReadCollection(ref reader, options, state, default(TCollection), false);

        private TCollection ReadCollection(ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options, JsonReadState state, TCollection existing, bool populate)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (default(TCollection) != null) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? typeof(TCollection));
                return default(TCollection);
            }
            string referenceId = null;
            if (reader.TokenType == JsonTokenType.StartObject && state.References != null)
            {
                // { "$id": "1", "$values": [...] } or { "$ref": "1" }
                reader.Read();
                string name = reader.TokenType == JsonTokenType.PropertyName ? reader.GetString() : null;
                if (name == "$ref")
                {
                    reader.Read();
                    string id = reader.GetString();
                    reader.Read();
                    if (!state.References.TryGetValue(id, out object target))
                        throw new JsonExceptionImpl("Reference '" + id + "' was not found.") { AppendPathInformation = true };
                    return (TCollection)target;
                }
                if (name == "$id")
                {
                    reader.Read();
                    referenceId = reader.GetString();
                    reader.Read();
                    name = reader.GetString();
                }
                if (name != "$values") throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? typeof(TCollection));
                reader.Read();
            }
            if (reader.TokenType != JsonTokenType.StartArray) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? typeof(TCollection));
            JsonConverterImpl<TElement> element = Element(options);
            List<TElement> elements = new List<TElement>();
            state.PushArray();
            int index = 0;
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndArray) break;
                state.SetIndex(index);
                state.CurrentValueType = typeof(TElement);
                elements.Add(element.ReadWithState(ref reader, typeof(TElement), options, state));
                index++;
            }
            state.Pop();
            TCollection result = populate ? AddElements(existing, elements) : FromElements(elements, state);
            if (referenceId != null)
            {
                state.References[referenceId] = result;
                reader.Read(); // EndObject of the $values wrapper
            }
            return result;
        }

        protected TCollection CreateOrThrow(JsonReadState state)
        {
            if (Create == null)
                throw new NotSupportedException("The collection type '" + typeof(TCollection).ToString() + "' is abstract, an interface, or is read only, and could not be instantiated and populated.");
            return Create();
        }
    }

    internal sealed class ArrayConverter<TElement> : CollectionConverter<TElement[], TElement>
    {
        protected override IEnumerable<TElement> Elements(TElement[] collection) => collection;
        protected override TElement[] FromElements(List<TElement> elements, JsonReadState state) => elements.ToArray();
    }

    internal sealed class ListConverter<TCollection, TElement> : CollectionConverter<TCollection, TElement> where TCollection : List<TElement>
    {
        protected override IEnumerable<TElement> Elements(TCollection collection) => collection;

        protected override TCollection FromElements(List<TElement> elements, JsonReadState state)
        {
            TCollection result = CreateOrThrow(state);
            result.AddRange(elements);
            return result;
        }

        internal override bool CanPopulate => true;

        protected override TCollection AddElements(TCollection existing, List<TElement> elements)
        {
            existing.AddRange(elements);
            return existing;
        }
    }

    internal sealed class ICollectionOfTConverter<TCollection, TElement> : CollectionConverter<TCollection, TElement> where TCollection : ICollection<TElement>
    {
        protected override IEnumerable<TElement> Elements(TCollection collection) => collection;

        protected override TCollection FromElements(List<TElement> elements, JsonReadState state)
        {
            ICollection<TElement> result;
            if (Create != null) result = Create();
            else if (typeof(TCollection) == typeof(ISet<TElement>)) result = new HashSet<TElement>();
            else
            {
                // IList<T>, ICollection<T>: .NET instantiates a List<T>.
                object list = elements;
                if (list is TCollection asCollection) return asCollection;
                return CreateOrThrow(state);
            }
            if (result.IsReadOnly)
                throw new NotSupportedException("The collection type '" + typeof(TCollection).ToString() + "' is abstract, an interface, or is read only, and could not be instantiated and populated.");
            foreach (TElement e in elements) result.Add(e);
            return (TCollection)result;
        }

        internal override bool CanPopulate => true;

        protected override TCollection AddElements(TCollection existing, List<TElement> elements)
        {
            if (existing.IsReadOnly)
                throw new NotSupportedException("The collection type '" + typeof(TCollection).ToString() + "' is abstract, an interface, or is read only, and could not be instantiated and populated.");
            foreach (TElement e in elements) existing.Add(e);
            return existing;
        }
    }

    internal sealed class IEnumerableOfTConverter<TCollection, TElement> : CollectionConverter<TCollection, TElement> where TCollection : IEnumerable<TElement>
    {
        internal Func<IEnumerable<TElement>, TCollection> CreateRange;

        protected override IEnumerable<TElement> Elements(TCollection collection) => collection;

        protected override TCollection FromElements(List<TElement> elements, JsonReadState state)
        {
            if (CreateRange != null) return CreateRange(elements);
            if (typeof(TCollection) == typeof(IReadOnlySet<TElement>))
            {
                object set = new HashSet<TElement>(elements);
                return (TCollection)set;
            }
            object list = elements;
            if (list is TCollection asCollection) return asCollection;
            return CreateOrThrow(state);
        }
    }

    internal sealed class QueueConverter<TCollection, TElement> : CollectionConverter<TCollection, TElement> where TCollection : Queue<TElement>
    {
        protected override IEnumerable<TElement> Elements(TCollection collection) => collection;

        protected override TCollection FromElements(List<TElement> elements, JsonReadState state)
        {
            TCollection result = CreateOrThrow(state);
            foreach (TElement e in elements) result.Enqueue(e);
            return result;
        }

        internal override bool CanPopulate => true;

        protected override TCollection AddElements(TCollection existing, List<TElement> elements)
        {
            foreach (TElement e in elements) existing.Enqueue(e);
            return existing;
        }
    }

    internal sealed class StackConverter<TCollection, TElement> : CollectionConverter<TCollection, TElement> where TCollection : Stack<TElement>
    {
        protected override IEnumerable<TElement> Elements(TCollection collection) => collection;

        protected override TCollection FromElements(List<TElement> elements, JsonReadState state)
        {
            TCollection result = CreateOrThrow(state);
            foreach (TElement e in elements) result.Push(e);
            return result;
        }

        internal override bool CanPopulate => true;

        protected override TCollection AddElements(TCollection existing, List<TElement> elements)
        {
            foreach (TElement e in elements) existing.Push(e);
            return existing;
        }
    }

    internal sealed class DictionaryConverter<TCollection, TKey, TValue> : JsonConverterImpl<TCollection> where TCollection : IEnumerable<KeyValuePair<TKey, TValue>>
    {
        internal JsonTypeInfoImpl<TCollection> TypeInfo;
        internal Func<TCollection> Create;
        internal Func<IEnumerable<KeyValuePair<TKey, TValue>>, TCollection> CreateRange;
        private JsonConverterImpl<TKey> _key;
        private JsonConverterImpl<TValue> _value;

        internal override bool IsInternalConverter => true;

        internal override JsonTypeInfoKind Kind => JsonTypeInfoKind.Dictionary;

        internal override bool IsNumberHandlingApplicable => Value(TypeInfo._options).IsNumberHandlingApplicable;

        private JsonConverterImpl<TKey> Key(JsonSerializerOptionsImpl options)
        {
            if (_key == null)
            {
                JsonTypeInfoImpl<TKey> info = (JsonTypeInfoImpl<TKey>)(TypeInfo._keyTypeInfo ?? options.GetTypeInfoForRuntimeType(typeof(TKey)));
                info.EnsureConfigured();
                _key = info.TypedConverter;
            }
            return _key;
        }

        private JsonConverterImpl<TValue> Value(JsonSerializerOptionsImpl options)
        {
            if (_value == null)
            {
                JsonTypeInfoImpl<TValue> info = (JsonTypeInfoImpl<TValue>)(TypeInfo._elementTypeInfo ?? options.GetTypeInfoForRuntimeType(typeof(TValue)));
                info.EnsureConfigured();
                _value = info.TypedConverter;
            }
            return _value;
        }

        public override TCollection Read(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options) =>
            ReadWithState(ref reader, typeToConvert, options, new JsonReadState(options));

        public override void Write(Utf8JsonWriterImpl writer, TCollection value, JsonSerializerOptionsImpl options) =>
            WriteWithState(writer, value, options, new JsonWriteState(options));

        internal override void WriteWithState(Utf8JsonWriterImpl writer, TCollection value, JsonSerializerOptionsImpl options, JsonWriteState state)
        {
            if (writer.CurrentDepth >= options.EffectiveMaxDepth)
                throw new JsonExceptionImpl(JsonSerializerHelpers.CycleDetected(options.EffectiveMaxDepth)) { AppendPathInformation = true };
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }
            JsonConverterImpl<TValue> valueConverter = Value(options);
            writer.WriteStartObject();
            if (state.ReferenceResolver != null && !CoreIntrinsics.IsValueType<TCollection>())
            {
                string id = state.ReferenceResolver.GetReference(value, out bool alreadyExists);
                if (alreadyExists)
                {
                    writer.WriteString("$ref", id);
                    writer.WriteEndObject();
                    return;
                }
                writer.WriteString("$id", id);
            }
            JsonNamingPolicyImpl keyPolicy = options.DictionaryKeyPolicy;
            foreach (KeyValuePair<TKey, TValue> kv in value)
            {
                string keyText;
                if (typeof(TKey) == typeof(string))
                {
                    keyText = (string)(object)kv.Key;
                    if (keyPolicy != null)
                    {
                        keyText = keyPolicy.ConvertName(keyText);
                        if (keyText == null)
                            throw new InvalidOperationException("The naming policy '" + keyPolicy.GetType().ToString() + "' cannot return null.");
                    }
                    writer.WritePropertyName(keyText);
                }
                else
                {
                    JsonConverterImpl<TKey> keyConverter = Key(options);
                    keyConverter.WriteAsPropertyName(writer, kv.Key, options);
                    keyText = kv.Key.ToString();
                }
                state.Push(keyText);
                valueConverter.WriteWithState(writer, kv.Value, options, state);
                state.Pop();
            }
            writer.WriteEndObject();
        }

        internal override TCollection ReadWithState(ref Utf8JsonReaderImpl reader, Type typeToConvert, JsonSerializerOptionsImpl options, JsonReadState state) =>
            ReadDictionary(ref reader, options, state, null);

        // Mutable dictionaries (IDictionary<TKey, TValue> and the concrete types implementing it) can be populated.
        internal override bool CanPopulate => CreateRange == null;

        internal override TCollection ReadInto(ref Utf8JsonReaderImpl reader, TCollection existing, JsonSerializerOptionsImpl options, JsonReadState state) =>
            ReadDictionary(ref reader, options, state, existing as IDictionary<TKey, TValue>);

        private TCollection ReadDictionary(ref Utf8JsonReaderImpl reader, JsonSerializerOptionsImpl options, JsonReadState state, IDictionary<TKey, TValue> existing)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                if (default(TCollection) != null) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? typeof(TCollection));
                return default(TCollection);
            }
            if (reader.TokenType != JsonTokenType.StartObject) throw JsonSerializerHelpers.UnableToConvert(state.CurrentValueType ?? typeof(TCollection));
            JsonConverterImpl<TValue> valueConverter = Value(options);
            IDictionary<TKey, TValue> target;
            List<KeyValuePair<TKey, TValue>> pairs = null;
            if (existing != null) target = existing;
            else if (CreateRange != null)
            {
                pairs = new List<KeyValuePair<TKey, TValue>>();
                target = null;
            }
            else if (Create != null) target = (IDictionary<TKey, TValue>)(object)Create();
            else target = new Dictionary<TKey, TValue>();
            string referenceId = null;
            while (true)
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.EndObject) break;
                string keyText = reader.GetString();
                if (state.References != null && keyText == "$id")
                {
                    reader.Read();
                    referenceId = reader.GetString();
                    continue;
                }
                if (state.References != null && keyText == "$ref")
                {
                    reader.Read();
                    string id = reader.GetString();
                    reader.Read();
                    if (!state.References.TryGetValue(id, out object referenced))
                        throw new JsonExceptionImpl("Reference '" + id + "' was not found.") { AppendPathInformation = true };
                    return (TCollection)referenced;
                }
                TKey key;
                state.PushProperty(keyText);
                state.CurrentValueType = typeof(TKey);
                if (typeof(TKey) == typeof(string)) key = (TKey)(object)keyText;
                else key = Key(options).ReadAsPropertyName(ref reader, typeof(TKey), options);
                reader.Read();
                state.CurrentValueType = typeof(TValue);
                TValue value = valueConverter.ReadWithState(ref reader, typeof(TValue), options, state);
                state.Pop();
                if (pairs != null) pairs.Add(new KeyValuePair<TKey, TValue>(key, value));
                else if (options.AllowDuplicateProperties) target[key] = value;
                else if (!target.TryAdd(key, value))
                {
                    state.PushProperty(keyText);
                    throw new JsonExceptionImpl("Duplicate properties not allowed during deserialization.");
                }
            }
            TCollection result = pairs != null ? CreateRange(pairs) : (TCollection)target;
            if (referenceId != null) state.References[referenceId] = result;
            return result;
        }
    }
}
