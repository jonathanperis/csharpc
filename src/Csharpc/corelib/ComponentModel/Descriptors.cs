// System.ComponentModel's MemberDescriptor, PropertyDescriptor, AttributeCollection and
// PropertyDescriptorCollection, ported from dotnet/runtime (System.ComponentModel.TypeConverter, MIT).
// csharpc serves that assembly from the corelib (it shadows TypeConverter and TypeDescriptor), so these
// are here for the libraries that derive from them (System.Data's column descriptors). TypeDescriptor's
// provider machinery isn't: the members that need it (editors, child properties, converter types by
// name, associations) throw NotSupportedException.
#nullable enable
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace CsharpcCoreLib
{



    [Shadows("System.ComponentModel.MemberDescriptor")]
    public abstract class MemberDescriptorImpl
    {
        private readonly string? _name;
        private readonly string _displayName;
        private readonly int _nameHash;
        private AttributeCollection? _attributeCollection;
        private Attribute[]? _attributes;
        private Attribute[]? _originalAttributes;
        private bool _attributesFiltered;
        private bool _attributesFilled;
        private int _metadataVersion;
        private string? _category;
        private string? _description;
        private readonly object _lockCookie = new object();

        protected MemberDescriptorImpl(string name) : this(name, null)
        {
        }

        protected MemberDescriptorImpl(string name, Attribute[]? attributes)
        {
            ArgumentNullException.ThrowIfNull(name);

            if (name.Length == 0)
            {
                throw new ArgumentException("Invalid member name", nameof(name));
            }

            _name = name;
            _displayName = name;
            _nameHash = name.GetHashCode();

            if (attributes != null)
            {
                _attributes = attributes;
                _attributesFiltered = false;
            }

            _originalAttributes = _attributes;
        }

        protected MemberDescriptorImpl(MemberDescriptor descr)
        {
            ArgumentNullException.ThrowIfNull(descr);

            _name = descr.Name;
            _displayName = _name;
            _nameHash = _name?.GetHashCode() ?? 0;

            _attributes = new Attribute[descr.Attributes.Count];
            descr.Attributes.CopyTo(_attributes, 0);

            _attributesFiltered = true;

            _originalAttributes = _attributes;
        }

        protected MemberDescriptorImpl(MemberDescriptor oldMemberDescriptor, Attribute[]? newAttributes)
        {
            ArgumentNullException.ThrowIfNull(oldMemberDescriptor);

            _name = oldMemberDescriptor.Name;
            _displayName = oldMemberDescriptor.DisplayName;
            _nameHash = _name.GetHashCode();

            List<Attribute> newList = new List<Attribute>();

            if (oldMemberDescriptor.Attributes.Count != 0)
            {
                foreach (Attribute o in oldMemberDescriptor.Attributes)
                {
                    newList.Add(o);
                }
            }

            if (newAttributes != null)
            {
                foreach (Attribute o in newAttributes)
                {
                    newList.Add(o);
                }
            }

            _attributes = new Attribute[newList.Count];
            newList.CopyTo(_attributes, 0);
            _attributesFiltered = false;

            _originalAttributes = _attributes;
        }

        protected virtual Attribute[]? AttributeArray
        {
            get
            {
                CheckAttributesValid();
                FilterAttributesIfNeeded();
                return _attributes;
            }
            set
            {
                lock (_lockCookie)
                {
                    _attributes = value;
                    _originalAttributes = value;
                    _attributesFiltered = false;
                    _attributeCollection = null;
                }
            }
        }

        public virtual AttributeCollection Attributes
        {
            get
            {
                CheckAttributesValid();
                AttributeCollection? attrs = _attributeCollection;
                if (attrs == null)
                {
                    lock (_lockCookie)
                    {
                        attrs = CreateAttributeCollection();
                        _attributeCollection = attrs;
                    }
                }
                return attrs;
            }
        }

        public virtual string Category => _category ??= ((CategoryAttribute)Attributes[typeof(CategoryAttribute)]!).Category;

        public virtual string Description => _description ??= ((DescriptionAttribute)Attributes[typeof(DescriptionAttribute)]!).Description;

        public virtual bool IsBrowsable => ((BrowsableAttribute)Attributes[typeof(BrowsableAttribute)]!).Browsable;

        public virtual string Name => _name ?? "";

        protected virtual int NameHashCode => _nameHash;
        internal int NameHashCodeOf => NameHashCode;

        public virtual bool DesignTimeOnly => (DesignOnlyAttribute.Yes.Equals(Attributes[typeof(DesignOnlyAttribute)]));

        public virtual string DisplayName
        {
            get
            {
                if (!(Attributes[typeof(DisplayNameAttribute)] is DisplayNameAttribute displayNameAttr) || displayNameAttr.IsDefaultAttribute())
                {
                    return _displayName;
                }
                return displayNameAttr.DisplayName;
            }
        }

        private void CheckAttributesValid()
        {
            if (_attributesFiltered)
            {
                if (_metadataVersion != 0)
                {
                    _attributesFilled = false;
                    _attributesFiltered = false;
                    _attributeCollection = null;
                }
            }
        }

        protected virtual AttributeCollection CreateAttributeCollection()
        {
            return new AttributeCollection(AttributeArray);
        }

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (this == obj)
            {
                return true;
            }
            if (obj == null)
            {
                return false;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            MemberDescriptorImpl mdObj = (MemberDescriptorImpl)obj;
            FilterAttributesIfNeeded();
            mdObj.FilterAttributesIfNeeded();

            if (mdObj._nameHash != _nameHash)
            {
                return false;
            }

            if ((mdObj._category == null) != (_category == null) ||
                (_category != null && !mdObj._category!.Equals(_category)))
            {
                return false;
            }

            if ((mdObj._description == null) != (_description == null) ||
                (_description != null && !mdObj._description!.Equals(_description)))
            {
                return false;
            }

            if ((mdObj._attributes == null) != (_attributes == null))
            {
                return false;
            }

            bool sameAttrs = true;

            if (_attributes != null)
            {
                if (_attributes.Length != mdObj._attributes!.Length)
                {
                    return false;
                }
                for (int i = 0; i < _attributes.Length; i++)
                {
                    if (!_attributes[i].Equals(mdObj._attributes[i]))
                    {
                        sameAttrs = false;
                        break;
                    }
                }
            }
            return sameAttrs;
        }

        protected virtual void FillAttributes(IList attributeList)
        {
            ArgumentNullException.ThrowIfNull(attributeList);

            if (_originalAttributes != null)
            {
                foreach (Attribute attr in _originalAttributes)
                {
                    attributeList.Add(attr);
                }
            }
        }

        private void FilterAttributesIfNeeded()
        {
            if (!_attributesFiltered)
            {
                List<Attribute> list;

                if (!_attributesFilled)
                {
                    list = new List<Attribute>();
                    try
                    {
                        FillAttributes(list);
                    }
                    catch (Exception)
                    {
                    }
                }
                else
                {
                    list = new List<Attribute>(_attributes!);
                }

                var map = new Dictionary<object, int>();

                for (int i = 0; i < list.Count;)
                {
                    int savedIndex;
                    object? typeId = list[i]?.TypeId;
                    if (typeId == null)
                    {
                        list.RemoveAt(i);
                    }
                    else if (!map.TryGetValue(typeId, out savedIndex))
                    {
                        map.Add(typeId, i);
                        i++;
                    }
                    else
                    {
                        list[savedIndex] = list[i];
                        list.RemoveAt(i);
                    }
                }

                Attribute[] newAttributes = list.ToArray();

                lock (_lockCookie)
                {
                    _attributes = newAttributes;
                    _attributesFiltered = true;
                    _attributesFilled = true;
                    _metadataVersion = 0;
                }
            }
        }
        protected static MethodInfo? FindMethod(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] Type componentClass,
            string name,
            Type[] args,
            Type returnType)
        {
            return FindMethod(componentClass, name, args, returnType, publicOnly: true);
        }

        protected static MethodInfo? FindMethod(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type componentClass,
            string name,
            Type[] args,
            Type returnType,
            bool publicOnly)
        {
            ArgumentNullException.ThrowIfNull(componentClass);

            MethodInfo? result;
            if (publicOnly)
            {
                result = componentClass.GetMethod(name, args);
            }
            else
            {
                result = componentClass.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
            }
            if (result != null && !result.ReturnType.IsEquivalentTo(returnType))
            {
                result = null;
            }
            return result;
        }

        public override int GetHashCode() => _nameHash;

        protected virtual object? GetInvocationTarget(Type type, object instance)
        {
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(instance);

            return ComponentModelSupport.GetAssociation(type, instance);
        }

        protected static ISite? GetSite(object? component) => (component as IComponent)?.Site;

        [Obsolete("MemberDescriptor.GetInvokee has been deprecated. Use GetInvocationTarget instead.")]
        protected static object GetInvokee(Type componentClass, object component)
        {
            ArgumentNullException.ThrowIfNull(componentClass);
            ArgumentNullException.ThrowIfNull(component);

            return ComponentModelSupport.GetAssociation(componentClass, component);
        }
    }




    [Shadows("System.ComponentModel.PropertyDescriptor")]
    public abstract class PropertyDescriptorImpl : MemberDescriptor
    {
        internal const string PropertyDescriptorPropertyTypeMessage = "PropertyDescriptor's PropertyType cannot be statically discovered.";

        private TypeConverter? _converter;
        private ConcurrentDictionary<object, EventHandler?>? _valueChangedHandlers;
        private object?[]? _editors;
        private Type[]? _editorTypes;
        private int _editorCount;
        private object? _syncObject;

        protected PropertyDescriptorImpl(string name, Attribute[]? attrs) : base(name, attrs)
        {
        }

        protected PropertyDescriptorImpl(MemberDescriptor descr) : base(descr)
        {
        }

        protected PropertyDescriptorImpl(MemberDescriptor descr, Attribute[]? attrs) : base(descr, attrs)
        {
        }

        private object SyncObject => _syncObject ?? Interlocked.CompareExchange(ref _syncObject, new object(), null) ?? _syncObject;

        public abstract Type ComponentType { get; }

        // A [TypeConverter] attribute names its converter by type name: csharpc can't create that by name yet,
        // so only the property type's own converter (TypeDescriptor.GetConverter) is served.
        public virtual TypeConverter Converter
        {
            get
            {
                AttributeCollection attrs = Attributes;
                if (_converter == null)
                {
                    foreach (Attribute attribute in attrs)
                        if (attribute.GetType().FullName == "System.ComponentModel.TypeConverterAttribute")
                            throw ComponentModelSupport.NotSupported("PropertyDescriptor.Converter of a property with [TypeConverter]");
                    _converter = TypeDescriptor.GetConverter(PropertyType);
                }
                return _converter;
            }
        }

        public virtual TypeConverter ConverterFromRegisteredType => throw ComponentModelSupport.NotSupported("PropertyDescriptor.ConverterFromRegisteredType");

        public virtual bool IsLocalizable => (LocalizableAttribute.Yes.Equals(Attributes[typeof(LocalizableAttribute)]));

        public abstract bool IsReadOnly { get; }

        public DesignerSerializationVisibility SerializationVisibility
        {
            get
            {
                DesignerSerializationVisibilityAttribute attr = (DesignerSerializationVisibilityAttribute)Attributes[typeof(DesignerSerializationVisibilityAttribute)]!;
                return attr.Visibility;
            }
        }

        public abstract Type PropertyType { get; }

        public virtual void AddValueChanged(object component, EventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(handler);

            lock (SyncObject)
            {
                _valueChangedHandlers ??= new ConcurrentDictionary<object, EventHandler?>(concurrencyLevel: 1, capacity: 0);
                _valueChangedHandlers.AddOrUpdate(component, handler, (k, v) => (EventHandler?)Delegate.Combine(v, handler));
            }
        }

        public abstract bool CanResetValue(object component);

        public override bool Equals([NotNullWhen(true)] object? obj)
        {
            try
            {
                if (obj == this)
                {
                    return true;
                }

                if (obj == null)
                {
                    return false;
                }

                // Assume that 90% of the time we will only do a .Equals(...) for
                // propertydescriptor vs. propertydescriptor... avoid the overhead
                // of an instanceof call.

                if (obj is PropertyDescriptor pd && ((MemberDescriptorImpl)(object)pd).NameHashCodeOf == NameHashCode
                    && pd.PropertyType == PropertyType
                    && pd.Name.Equals(Name))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        protected object? CreateInstance(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        {
            Type[] typeArgs = new Type[] { typeof(Type) };
            ConstructorInfo? ctor = type.GetConstructor(typeArgs);
            if (ctor != null)
            {
                throw ComponentModelSupport.NotSupported("PropertyDescriptor.CreateInstance");
            }

            throw ComponentModelSupport.NotSupported("PropertyDescriptor.CreateInstance");
        }

        protected override void FillAttributes(IList attributeList)
        {
            // Each time we fill our attributes, we should clear our cached
            // stuff.
            _converter = null;
            _editors = null;
            _editorTypes = null;
            _editorCount = 0;

            base.FillAttributes(attributeList);
        }
        public PropertyDescriptorCollection GetChildProperties() => GetChildProperties(null, null);
        public PropertyDescriptorCollection GetChildProperties(Attribute[] filter) => GetChildProperties(null, filter);
        public PropertyDescriptorCollection GetChildProperties(object instance) => GetChildProperties(instance, null);
        public virtual PropertyDescriptorCollection GetChildProperties(object? instance, Attribute[]? filter)
        {
            if (instance == null)
            {
                throw ComponentModelSupport.NotSupported("PropertyDescriptor.GetChildProperties");
            }
            else
            {
                throw ComponentModelSupport.NotSupported("PropertyDescriptor.GetChildProperties");
            }
        }
        public virtual object? GetEditor(Type editorBaseType)
        {
            object? editor = null;

            // Always grab the attribute collection first here, because if the metadata version
            // changes it will invalidate our editor cache.
            AttributeCollection attrs = Attributes;

            // Check the editors we've already created for this type.
            if (_editorTypes != null)
            {
                for (int i = 0; i < _editorCount; i++)
                {
                    if (_editorTypes[i] == editorBaseType)
                    {
                        return _editors![i];
                    }
                }
            }

            // If one wasn't found, then we must go through the attributes.
            if (editor == null)
            {
                for (int i = 0; i < attrs.Count; i++)
                {
                    if (!(attrs[i] is EditorAttribute attr))
                    {
                        continue;
                    }

                    Type? editorType = GetTypeFromName(attr.EditorBaseTypeName);

                    if (editorBaseType == editorType)
                    {
                        Type? type = GetTypeFromName(attr.EditorTypeName);
                        if (type != null)
                        {
                            editor = CreateInstance(type);
                            break;
                        }
                    }
                }

                // Now, if we failed to find it in our own attributes, go to the
                // component descriptor.
                if (editor == null) throw ComponentModelSupport.NotSupported("PropertyDescriptor.GetEditor");

                // Now, another slot in our editor cache for next time
                if (_editorTypes == null)
                {
                    _editorTypes = new Type[5];
                    _editors = new object[5];
                }

                if (_editorCount >= _editorTypes.Length)
                {
                    Type[] newTypes = new Type[_editorTypes.Length * 2];
                    object[] newEditors = new object[_editors!.Length * 2];
                    Array.Copy(_editorTypes, newTypes, _editorTypes.Length);
                    Array.Copy(_editors, newEditors, _editors.Length);
                    _editorTypes = newTypes;
                    _editors = newEditors;
                }

                _editorTypes[_editorCount] = editorBaseType;
                _editors![_editorCount++] = editor;
            }

            return editor;
        }

        public override int GetHashCode() => NameHashCode ^ PropertyType.GetHashCode();

        protected override object? GetInvocationTarget(Type type, object instance)
        {
            object? target = base.GetInvocationTarget(type, instance);
            if (target is ICustomTypeDescriptor td)
            {
                target = td.GetPropertyOwner(CoreIntrinsics.ReinterpretAs<PropertyDescriptor>(this));
            }

            return target;
        }
        protected Type? GetTypeFromName(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] string? typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            //  try the generic method.
            Type? typeFromGetType = Type.GetType(typeName);

            // If we didn't get a type from the generic method, or if the assembly we found the type
            // in is the same as our Component's assembly, use or Component's assembly instead. This is
            // because the CLR may have cached an older version if the assembly's version number didn't change
            Type? typeFromComponent = null;
            if (ComponentType != null)
            {
                if ((typeFromGetType == null) ||
                    (ComponentType.Assembly.FullName!.Equals(typeFromGetType.Assembly.FullName)))
                {
                    int comma = typeName.IndexOf(',');

                    if (comma != -1)
                        typeName = typeName.Substring(0, comma);

                    typeFromComponent = ComponentType.Assembly.GetType(typeName);
                }
            }

            return typeFromComponent ?? typeFromGetType;
        }

        public abstract object? GetValue(object? component);

        protected virtual void OnValueChanged(object? component, EventArgs e)
        {
            if (component != null)
            {
                _valueChangedHandlers?.GetValueOrDefault(component, defaultValue: null)?.Invoke(component, e);
            }
        }

        public virtual void RemoveValueChanged(object component, EventHandler handler)
        {
            ArgumentNullException.ThrowIfNull(component);
            ArgumentNullException.ThrowIfNull(handler);

            if (_valueChangedHandlers != null)
            {
                lock (SyncObject)
                {
                    EventHandler? h = _valueChangedHandlers.GetValueOrDefault(component, defaultValue: null);
                    h = (EventHandler?)Delegate.Remove(h, handler);
                    if (h != null)
                    {
                        _valueChangedHandlers[component] = h;
                    }
                    else
                    {
                        _valueChangedHandlers.Remove(component, out EventHandler? _);
                    }
                }
            }
        }

        protected internal EventHandler? GetValueChangedHandler(object component)
        {
            if (component != null && _valueChangedHandlers != null)
            {
                return _valueChangedHandlers.GetValueOrDefault(component, defaultValue: null);
            }
            else
            {
                return null;
            }
        }

        public abstract void ResetValue(object component);

        public abstract void SetValue(object? component, object? value);

        public abstract bool ShouldSerializeValue(object component);

        public virtual bool SupportsChangeEvents => false;
    }




    [Shadows("System.ComponentModel.AttributeCollection")]
    public class AttributeCollectionImpl : ICollection, IEnumerable
    {
        internal const string FilterRequiresUnreferencedCodeMessage = "The public parameterless constructor or the 'Default' static field may be trimmed from the Attribute's Type.";

        public static readonly AttributeCollection Empty = new AttributeCollection(null);

        private static Dictionary<Type, Attribute?>? s_defaultAttributes;

        private readonly Attribute[] _attributes;

        private static readonly object s_internalSyncObject = new object();

        private struct AttributeEntry
        {
            public Type type;
            public int index;
        }

        private const int FoundTypesLimit = 5;

        private AttributeEntry[]? _foundAttributeTypes;

        private int _index;

        public AttributeCollectionImpl(params Attribute[]? attributes)
        {
            _attributes = attributes ?? Array.Empty<Attribute>();

            for (int idx = 0; idx < _attributes.Length; idx++)
            {
                ArgumentNullException.ThrowIfNull(_attributes[idx], nameof(attributes));
            }
        }

        protected AttributeCollectionImpl() : this(Array.Empty<Attribute>())
        {
        }

        public static AttributeCollection FromExisting(AttributeCollection existing, params Attribute[]? newAttributes)
        {
            ArgumentNullException.ThrowIfNull(existing);

            newAttributes ??= Array.Empty<Attribute>();

            Attribute[] newArray = new Attribute[existing.Count + newAttributes.Length];
            int actualCount = existing.Count;
            existing.CopyTo(newArray, 0);

            for (int idx = 0; idx < newAttributes.Length; idx++)
            {
                ArgumentNullException.ThrowIfNull(newAttributes[idx], nameof(newAttributes));

                // We must see if this attribute is already in the existing
                // array. If it is, we replace it.
                bool match = false;
                for (int existingIdx = 0; existingIdx < existing.Count; existingIdx++)
                {
                    if (newArray[existingIdx].TypeId.Equals(newAttributes[idx].TypeId))
                    {
                        match = true;
                        newArray[existingIdx] = newAttributes[idx];
                        break;
                    }
                }

                if (!match)
                {
                    newArray[actualCount++] = newAttributes[idx];
                }
            }

            // Now, if we collapsed some attributes, create a new array.
            Attribute[] attributes;
            if (actualCount < newArray.Length)
            {
                attributes = new Attribute[actualCount];
                Array.Copy(newArray, attributes, actualCount);
            }
            else
            {
                attributes = newArray;
            }

            return new AttributeCollection(attributes);
        }

        protected internal virtual Attribute[] Attributes => _attributes;

        public int Count => Attributes.Length;

        public virtual Attribute this[int index] => Attributes[index];

        public virtual Attribute? this[[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields)] Type attributeType]
        {
            get
            {
                ArgumentNullException.ThrowIfNull(attributeType);

                lock (s_internalSyncObject)
                {
                    // 2 passes here for perf. Really!  first pass, we just
                    // check equality, and if we don't find it, then we
                    // do the IsAssignableFrom dance. Turns out that's
                    // a relatively expensive call and we try to avoid it
                    // since we rarely encounter derived attribute types
                    // and this list is usually short.
                    _foundAttributeTypes ??= new AttributeEntry[FoundTypesLimit];

                    int ind = 0;

                    for (; ind < FoundTypesLimit; ind++)
                    {
                        if (_foundAttributeTypes[ind].type == attributeType)
                        {
                            int index = _foundAttributeTypes[ind].index;
                            if (index != -1)
                            {
                                return Attributes[index];
                            }
                            else
                            {
                                return GetDefaultAttribute(attributeType);
                            }
                        }
                        if (_foundAttributeTypes[ind].type == null)
                            break;
                    }

                    ind = _index++;

                    if (_index >= FoundTypesLimit)
                    {
                        _index = 0;
                    }

                    _foundAttributeTypes[ind].type = attributeType;

                    int count = Attributes.Length;
                    for (int i = 0; i < count; i++)
                    {
                        Attribute attribute = Attributes[i];
                        Type aType = attribute.GetType();
                        if (aType == attributeType)
                        {
                            _foundAttributeTypes[ind].index = i;
                            return attribute;
                        }
                    }

                    // Now check the hierarchies.
                    for (int i = 0; i < count; i++)
                    {
                        Attribute attribute = Attributes[i];
                        if (attributeType.IsInstanceOfType(attribute))
                        {
                            _foundAttributeTypes[ind].index = i;
                            return attribute;
                        }
                    }

                    _foundAttributeTypes[ind].index = -1;
                    return GetDefaultAttribute(attributeType);
                }
            }
        }
        public bool Contains(Attribute? attribute)
        {
            if (attribute == null)
            {
                return false;
            }

            Attribute? attr = this[attribute.GetType()];
            return attr != null && attr.Equals(attribute);
        }
        public bool Contains(Attribute[]? attributes)
        {
            if (attributes == null)
            {
                return true;
            }

            for (int i = 0; i < attributes.Length; i++)
            {
                if (!Contains(attributes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        protected Attribute? GetDefaultAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicFields)] Type attributeType)
        {
            ArgumentNullException.ThrowIfNull(attributeType);

            lock (s_internalSyncObject)
            {
                s_defaultAttributes ??= new Dictionary<Type, Attribute?>();

                // If we have already encountered this, use what's in the table.
                if (s_defaultAttributes.TryGetValue(attributeType, out Attribute? defaultAttribute))
                {
                    return defaultAttribute;
                }

                Attribute? attr = null;

                // Not in the table, so do the legwork to discover the default value.
                Type reflect = attributeType;
                FieldInfo? field = reflect.GetField("Default", BindingFlags.Public | BindingFlags.Static | BindingFlags.GetField);

                if (field != null && field.IsStatic)
                {
                    attr = (Attribute?)field.GetValue(null);
                }
                else
                {
                    ConstructorInfo? ci = reflect.UnderlyingSystemType.GetConstructor(Type.EmptyTypes);
                    if (ci != null)
                    {
                        attr = (Attribute)ci.Invoke(Array.Empty<object>());

                        // If we successfully created, verify that it is the
                        // default. Attributes don't have to abide by this rule.
                        if (!attr.IsDefaultAttribute())
                        {
                            attr = null;
                        }
                    }
                }

                s_defaultAttributes[attributeType] = attr;
                return attr;
            }
        }

        public IEnumerator GetEnumerator() => Attributes.GetEnumerator();

        public bool Matches(Attribute? attribute)
        {
            for (int i = 0; i < Attributes.Length; i++)
            {
                if (Attributes[i].Match(attribute))
                {
                    return true;
                }
            }
            return false;
        }

        public bool Matches(Attribute[]? attributes)
        {
            if (attributes == null)
            {
                return true;
            }

            for (int i = 0; i < attributes.Length; i++)
            {
                if (!Matches(attributes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        int ICollection.Count => Count;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(Array array, int index) => Array.Copy(Attributes, 0, array, index, Attributes.Length);
    }




    [Shadows("System.ComponentModel.PropertyDescriptorCollection")]
    public class PropertyDescriptorCollectionImpl : ICollection, IList, IDictionary
    {
        public static readonly PropertyDescriptorCollection Empty = new PropertyDescriptorCollection(null, true);

        private Hashtable? _cachedFoundProperties;
        private bool _cachedIgnoreCase;
        private PropertyDescriptor[] _properties;
        private readonly string[]? _namedSort;
        private readonly IComparer? _comparer;
        private bool _propsOwned;
        private bool _needSort;
        private readonly bool _readOnly;

        private readonly object _internalSyncObject = new object();

        public PropertyDescriptorCollectionImpl(PropertyDescriptor[]? properties)
        {
            if (properties == null)
            {
                _properties = Array.Empty<PropertyDescriptor>();
                Count = 0;
            }
            else
            {
                _properties = properties;
                Count = properties.Length;
            }
            _propsOwned = true;
        }

        public PropertyDescriptorCollectionImpl(PropertyDescriptor[]? properties, bool readOnly) : this(properties)
        {
            _readOnly = readOnly;
        }

        private PropertyDescriptorCollectionImpl(PropertyDescriptor[] properties, int propCount, string[]? namedSort, IComparer? comparer)
        {
            _propsOwned = false;
            if (namedSort != null)
            {
                _namedSort = (string[])namedSort.Clone();
            }
            _comparer = comparer;
            _properties = properties;
            Count = propCount;
            _needSort = true;
        }

        public int Count { get; private set; }

        public virtual PropertyDescriptor this[int index]
        {
            get
            {
                if (index >= Count)
                {
                    throw new IndexOutOfRangeException();
                }
                EnsurePropsOwned();
                return _properties[index]!;
            }
        }

        public virtual PropertyDescriptor? this[string name] => Find(name, false);

        public int Add(PropertyDescriptor value)
        {
            if (_readOnly)
            {
                throw new NotSupportedException();
            }

            EnsureSize(Count + 1);
            _properties[Count++] = value;
            return Count - 1;
        }

        public void Clear()
        {
            if (_readOnly)
            {
                throw new NotSupportedException();
            }

            Count = 0;
            _cachedFoundProperties = null;
        }

        public bool Contains(PropertyDescriptor value) => IndexOf(value) >= 0;

        public void CopyTo(Array array, int index)
        {
            EnsurePropsOwned();
            Array.Copy(_properties, 0, array, index, Count);
        }

        private void EnsurePropsOwned()
        {
            if (!_propsOwned)
            {
                _propsOwned = true;
                if (_properties != null)
                {
                    PropertyDescriptor[] newProps = new PropertyDescriptor[Count];
                    Array.Copy(_properties, newProps, Count);
                    _properties = newProps;
                }
            }

            if (_needSort)
            {
                _needSort = false;
                InternalSort(_namedSort);
            }
        }

        private void EnsureSize(int sizeNeeded)
        {
            if (sizeNeeded <= _properties.Length)
            {
                return;
            }

            if (_properties.Length == 0)
            {
                Count = 0;
                _properties = new PropertyDescriptor[sizeNeeded];
                return;
            }

            EnsurePropsOwned();

            int newSize = Math.Max(sizeNeeded, _properties.Length * 2);
            PropertyDescriptor[] newProps = new PropertyDescriptor[newSize];
            Array.Copy(_properties, newProps, Count);
            _properties = newProps;
        }

        public virtual PropertyDescriptor? Find(string name, bool ignoreCase)
        {
            lock (_internalSyncObject)
            {
                PropertyDescriptor? p = null;

                if (_cachedFoundProperties == null || _cachedIgnoreCase != ignoreCase)
                {
                    _cachedIgnoreCase = ignoreCase;
                    if (ignoreCase)
                    {
                        _cachedFoundProperties = new Hashtable(StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        _cachedFoundProperties = new Hashtable();
                    }
                }

                // first try to find it in the cache
                //
                object? cached = _cachedFoundProperties[name];

                if (cached != null)
                {
                    return (PropertyDescriptor)cached;
                }

                // Now start walking from where we last left off, filling
                // the cache as we go.
                //
                for (int i = 0; i < Count; i++)
                {
                    if (ignoreCase)
                    {
                        if (string.Equals(_properties[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        {
                            _cachedFoundProperties[name] = _properties[i];
                            p = _properties[i];
                            break;
                        }
                    }
                    else
                    {
                        if (_properties[i].Name.Equals(name))
                        {
                            _cachedFoundProperties[name] = _properties[i];
                            p = _properties[i];
                            break;
                        }
                    }
                }

                return p;
            }
        }

        public int IndexOf(PropertyDescriptor? value) => Array.IndexOf(_properties, value, 0, Count);

        public void Insert(int index, PropertyDescriptor value)
        {
            if (_readOnly)
            {
                throw new NotSupportedException();
            }

            EnsureSize(Count + 1);
            if (index < Count)
            {
                Array.Copy(_properties, index, _properties, index + 1, Count - index);
            }
            _properties[index] = value;
            Count++;
        }

        public void Remove(PropertyDescriptor? value)
        {
            if (_readOnly)
            {
                throw new NotSupportedException();
            }

            int index = IndexOf(value);

            if (index != -1)
            {
                RemoveAt(index);
            }
        }

        public void RemoveAt(int index)
        {
            if (_readOnly)
            {
                throw new NotSupportedException();
            }

            if (index < Count - 1)
            {
                Array.Copy(_properties, index + 1, _properties, index, Count - index - 1);
            }
            _properties[Count - 1] = null!;
            Count--;
        }

        public virtual PropertyDescriptorCollection Sort()
        {
            return CoreIntrinsics.ReinterpretAs<PropertyDescriptorCollection>(new PropertyDescriptorCollectionImpl(_properties, Count, _namedSort, _comparer));
        }


        public virtual PropertyDescriptorCollection Sort(string[]? names)
        {
            return CoreIntrinsics.ReinterpretAs<PropertyDescriptorCollection>(new PropertyDescriptorCollectionImpl(_properties, Count, names, _comparer));
        }

        public virtual PropertyDescriptorCollection Sort(string[]? names, IComparer? comparer)
        {
            return CoreIntrinsics.ReinterpretAs<PropertyDescriptorCollection>(new PropertyDescriptorCollectionImpl(_properties, Count, names, comparer));
        }

        public virtual PropertyDescriptorCollection Sort(IComparer? comparer)
        {
            return CoreIntrinsics.ReinterpretAs<PropertyDescriptorCollection>(new PropertyDescriptorCollectionImpl(_properties, Count, _namedSort, comparer));
        }

        protected void InternalSort(string[]? names)
        {
            if (_properties.Length == 0)
            {
                return;
            }

            InternalSort(_comparer);

            if (names != null && names.Length > 0)
            {
                List<PropertyDescriptor?> propList = new List<PropertyDescriptor?>(_properties);
                int foundCount = 0;
                int propCount = _properties.Length;

                for (int i = 0; i < names.Length; i++)
                {
                    for (int j = 0; j < propCount; j++)
                    {
                        PropertyDescriptor? currentProp = propList[j];

                        // Found a matching property. Here, we add it to our array. We also
                        // mark it as null in our array list so we don't add it twice later.
                        //
                        if (currentProp != null && currentProp.Name.Equals(names[i]))
                        {
                            _properties[foundCount++] = currentProp;
                            propList[j] = null;
                            break;
                        }
                    }
                }

                // At this point we have filled in the first "foundCount" number of propeties, one for each
                // name in our name array. If a name didn't match, then it is ignored. Next, we must fill
                // in the rest of the properties. We now have a sparse array containing the remainder, so
                // it's easy.
                //
                for (int i = 0; i < propCount; i++)
                {
                    if (propList[i] != null)
                    {
                        _properties[foundCount++] = propList[i]!;
                    }
                }

                Debug.Assert(foundCount == propCount, "We did not completely fill our property array");
            }
        }

        protected void InternalSort(IComparer? sorter)
        {
            if (sorter == null)
            {
                Array.Sort(_properties, new ComponentModelSupport.Sorter(ComponentModelSupport.MemberNameComparer.Instance)); // TypeDescriptor.SortDescriptorArray
            }
            else
            {
                Array.Sort(_properties, new ComponentModelSupport.Sorter(sorter));
            }
        }

        public virtual IEnumerator GetEnumerator()
        {
            EnsurePropsOwned();
            // we can only return an enumerator on the props we actually have...
            if (_properties.Length != Count)
            {
                PropertyDescriptor[] enumProps = new PropertyDescriptor[Count];
                Array.Copy(_properties, enumProps, Count);
                return enumProps.GetEnumerator();
            }
            return _properties.GetEnumerator();
        }

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => null!;

        int ICollection.Count => Count;

        void IList.Clear() => Clear();

        void IDictionary.Clear() => Clear();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        void IList.RemoveAt(int index) => RemoveAt(index);

        void IDictionary.Add(object key, object? value)
        {
            if (!(value is PropertyDescriptor newProp))
            {
                throw new ArgumentException(nameof(value));
            }
            Add(newProp);
        }

        bool IDictionary.Contains(object key)
        {
            if (key is string)
            {
                return this[(string)key] != null;
            }
            return false;
        }

        IDictionaryEnumerator IDictionary.GetEnumerator() => new PropertyDescriptorEnumerator(this);

        bool IDictionary.IsFixedSize => _readOnly;

        bool IDictionary.IsReadOnly => _readOnly;

        object? IDictionary.this[object key]
        {
            get
            {
                if (key is string)
                {
                    return this[(string)key];
                }
                return null;
            }

            set
            {
                if (_readOnly)
                {
                    throw new NotSupportedException();
                }

                if (value != null && !(value is PropertyDescriptor))
                {
                    throw new ArgumentException(nameof(value));
                }

                int index = -1;

                if (key is int)
                {
                    index = (int)key;

                    if (index < 0 || index >= Count)
                    {
                        throw new IndexOutOfRangeException();
                    }
                }
                else if (key is string)
                {
                    for (int i = 0; i < Count; i++)
                    {
                        if (_properties[i]!.Name.Equals((string)key))
                        {
                            index = i;
                            break;
                        }
                    }
                }
                else
                {
                    throw new ArgumentException(nameof(key));
                }

                if (index == -1)
                {
                    Add((PropertyDescriptor)value!);
                }
                else
                {
                    EnsurePropsOwned();
                    _properties[index] = (PropertyDescriptor)value!;
                    if (_cachedFoundProperties != null && key is string)
                    {
                        _cachedFoundProperties[key] = value;
                    }
                }
            }
        }

        ICollection IDictionary.Keys
        {
            get
            {
                string[] keys = new string[Count];
                for (int i = 0; i < Count; i++)
                {
                    keys[i] = _properties[i]!.Name;
                }
                return keys;
            }
        }

        ICollection IDictionary.Values
        {
            get
            {
                // We can only return an enumerator on the props we actually have.
                if (_properties.Length != Count)
                {
                    PropertyDescriptor[] newProps = new PropertyDescriptor[Count];
                    Array.Copy(_properties, newProps, Count);
                    return newProps;
                }
                else
                {
                    return (ICollection)_properties.Clone();
                }
            }
        }

        void IDictionary.Remove(object key)
        {
            if (key is string)
            {
                PropertyDescriptor? pd = this[(string)key];
                if (pd != null)
                {
                    ((IList)this).Remove(pd);
                }
            }
        }

        int IList.Add(object? value) => Add((PropertyDescriptor)value!);

        bool IList.Contains(object? value) => Contains((PropertyDescriptor)value!);

        int IList.IndexOf(object? value) => IndexOf((PropertyDescriptor)value!);

        void IList.Insert(int index, object? value) => Insert(index, (PropertyDescriptor)value!);

        bool IList.IsReadOnly => _readOnly;

        bool IList.IsFixedSize => _readOnly;

        void IList.Remove(object? value) => Remove((PropertyDescriptor?)value);

        object? IList.this[int index]
        {
            get => this[index];
            set
            {
                if (_readOnly)
                {
                    throw new NotSupportedException();
                }

                if (index >= Count)
                {
                    throw new IndexOutOfRangeException();
                }


                if (value != null && !(value is PropertyDescriptor))
                {
                    throw new ArgumentException(nameof(value));
                }

                EnsurePropsOwned();
                _properties[index] = (PropertyDescriptor)value!;
            }
        }

        private sealed class PropertyDescriptorEnumerator : IDictionaryEnumerator
        {
            private readonly PropertyDescriptorCollectionImpl _owner;
            private int _index = -1;

            public PropertyDescriptorEnumerator(PropertyDescriptorCollectionImpl owner)
            {
                _owner = owner;
            }

            public object Current => Entry;

            public DictionaryEntry Entry
            {
                get
                {
                    PropertyDescriptor curProp = _owner[_index];
                    return new DictionaryEntry(curProp.Name, curProp);
                }
            }

            public object Key => _owner[_index].Name;

            public object Value => _owner[_index].Name;

            public bool MoveNext()
            {
                if (_index < (_owner.Count - 1))
                {
                    _index++;
                    return true;
                }
                return false;
            }

            public void Reset() => _index = -1;
        }
    }


    internal static class ComponentModelSupport
    {
        internal static NotSupportedException NotSupported(string what) => new NotSupportedException(what + " is not supported by csharpc yet.");

        // TypeDescriptor.GetAssociation without registered associations: the instance itself.
        internal static object GetAssociation(Type type, object instance)
        {
            if (!type.IsInstanceOfType(instance)) throw NotSupported("TypeDescriptor.GetAssociation of '" + type + "' for an object of another type");
            return instance;
        }

        internal sealed class Sorter(IComparer comparer) : IComparer<PropertyDescriptor>
        {
            public int Compare(PropertyDescriptor? x, PropertyDescriptor? y) => comparer.Compare(x, y);
        }

        // TypeDescriptor.MemberDescriptorComparer: by name, invariant culture.
        internal sealed class MemberNameComparer : IComparer
        {
            public static readonly MemberNameComparer Instance = new MemberNameComparer();
            public int Compare(object? left, object? right) =>
                string.Compare((left as MemberDescriptor)?.Name, (right as MemberDescriptor)?.Name, false, CultureInfo.InvariantCulture);
        }
    }
}
