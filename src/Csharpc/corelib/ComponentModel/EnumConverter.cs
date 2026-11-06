// System.ComponentModel.EnumConverter (System.ComponentModel.TypeConverter/EnumConverter.cs, MIT) over the run-time enum
// table (EnumRuntime: Enum.Parse, IsDefined, [Flags] of an enum known only as a Type). The string conversions are .NET's;
// what needs reflection over the enum's fields (GetStandardValues filters [Browsable(false)] members) or a
// MemberInfo invocation (InstanceDescriptor), and the Enum[] conversion, are explicit NotSupportedExceptions.
#nullable disable
using System;
using System.ComponentModel;
using System.Globalization;

namespace CsharpcCoreLib
{
    [Shadows("System.ComponentModel.EnumConverter")]
    public class EnumConverterImpl : TypeConverterImpl
    {
        public EnumConverterImpl(Type type)
        {
            if (!type.IsEnum && !type.Equals(typeof(Enum))) throw new ArgumentException("Type provided must be an Enum.");
            EnumType = type;
        }

        protected Type EnumType { get; }

        protected TypeConverter.StandardValuesCollection Values { get; set; }

        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
        {
            if (sourceType == typeof(string) || sourceType == typeof(Enum[])) return true;
            return base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
        {
            if (destinationType == typeof(Enum[]) || (destinationType != null && destinationType.FullName == "System.ComponentModel.Design.Serialization.InstanceDescriptor")) return true;
            return base.CanConvertTo(context, destinationType);
        }

        protected virtual System.Collections.IComparer Comparer => System.Collections.Comparer.DefaultInvariant;

        private static long GetEnumValue(bool isUnderlyingTypeUInt64, object enumVal, CultureInfo culture) =>
            isUnderlyingTypeUInt64 ? unchecked((long)Convert.ToUInt64(enumVal, culture)) : Convert.ToInt64(enumVal, culture);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string strValue)
            {
                try
                {
                    if (strValue.Contains(','))
                    {
                        bool isUnderlyingTypeUInt64 = Enum.GetUnderlyingType(EnumType) == typeof(ulong);
                        long convertedValue = 0;
                        foreach (Range v in strValue.AsSpan().Split(','))
                            convertedValue |= GetEnumValue(isUnderlyingTypeUInt64, Enum.Parse(EnumType, strValue.AsSpan(v), true), culture);
                        return Enum.ToObject(EnumType, convertedValue);
                    }
                    return Enum.Parse(EnumType, strValue, true);
                }
                catch (Exception e)
                {
                    throw new FormatException(strValue + " is not a valid value for " + EnumType.Name + ".", e);
                }
            }
            else if (value is Enum[])
            {
                bool isUnderlyingTypeUInt64 = Enum.GetUnderlyingType(EnumType) == typeof(ulong);
                long finalValue = 0;
                foreach (Enum e in (Enum[])value) finalValue |= GetEnumValue(isUnderlyingTypeUInt64, e, culture);
                return Enum.ToObject(EnumType, finalValue);
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);

            if (destinationType == typeof(string) && value != null)
            {
                // An undefined value is an error unless the enum is a flags enum.
                if (!EnumRuntime.IsFlags(EnumType) && !EnumRuntime.IsDefined(EnumType, value))
                    throw new ArgumentException("The value '" + value + "' is not a valid value for the enum '" + EnumType.Name + "'.");
                // Enum.Format(type, value, "G")
                if (value is Enum && value.GetType() != EnumType)
                    throw new ArgumentException("Object must be the same type as the enum. The type passed in was '" + value.GetType() + "'; the enum type was '" + EnumType + "'.");
                return (value is Enum ? value : Enum.ToObject(EnumType, value)).ToString();
            }
            if ((destinationType == typeof(Enum[]) || destinationType.FullName == "System.ComponentModel.Design.Serialization.InstanceDescriptor") && value != null)
                throw new NotSupportedException("EnumConverter.ConvertTo(" + destinationType.Name + ") is not supported by csharpc yet.");
            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override TypeConverter.StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            if (Values != null) return Values;
            throw new NotSupportedException("EnumConverter.GetStandardValues is not supported by csharpc yet: it lists the enum's fields through reflection (honoring [Browsable(false)]).");
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => !EnumRuntime.IsFlags(EnumType);

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;

        public override bool IsValid(ITypeDescriptorContext context, object value) => EnumRuntime.IsDefined(EnumType, value);
    }
}
