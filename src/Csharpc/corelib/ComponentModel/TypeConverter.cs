// System.ComponentModel.TypeConverter / TypeDescriptor: enough for Microsoft.Extensions.Configuration.Binder
// (GetConverter → CanConvertFrom(typeof(string)) → ConvertFromInvariantString). Shadowing these keeps
// System.ComponentModel.TypeConverter.dll out of the closed world (ReflectTypeDescriptionProvider and
// dozens of Half/Int128/COM APIs). Full design-time TypeConverter surface stays out of scope.
#nullable disable
using System;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.ComponentModel.TypeConverter")]
    public class TypeConverterImpl
    {
        public TypeConverterImpl() { }

        public bool CanConvertFrom(Type sourceType) => CanConvertFrom(null, sourceType);

        public virtual bool CanConvertFrom(object context, Type sourceType) => false;

        public object ConvertFromInvariantString(string text) =>
            ConvertFromString(null, CultureInfo.InvariantCulture, text);

        public object ConvertFromString(string text) =>
            ConvertFromString(null, CultureInfo.CurrentCulture, text);

        public virtual object ConvertFromString(object context, CultureInfo culture, string text) =>
            ConvertFrom(context, culture, text);

        public virtual object ConvertFrom(object context, CultureInfo culture, object value)
        {
            throw new NotSupportedException(GetType().Name + " cannot convert from " +
                (value == null ? "(null)" : value.GetType().FullName) + ".");
        }
    }

    /// <summary>TypeConverter for System.String: identity from a string (Binder's ConvertValue path).</summary>
    internal sealed class StringConverterImpl : TypeConverterImpl
    {
        public override bool CanConvertFrom(object context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override object ConvertFrom(object context, CultureInfo culture, object value)
        {
            if (value is string) return value;
            return base.ConvertFrom(context, culture, value);
        }
    }

    [Shadows("System.ComponentModel.TypeDescriptor")]
    public static class TypeDescriptorImpl
    {
        // Return type is the shadow; CoreLib matching ignores return types (docs/12-corelib.md).
        public static TypeConverterImpl GetConverter(Type type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (type == typeof(string)) return new StringConverterImpl();
            // Other types: a converter that cannot convert from string. Binder then fails that
            // property with its usual message rather than pulling the real TypeConverter.dll.
            return new TypeConverterImpl();
        }

        public static TypeConverterImpl GetConverter(object component)
        {
            ArgumentNullException.ThrowIfNull(component);
            return GetConverter(component.GetType());
        }
    }
}
