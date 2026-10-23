// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// The messages of System.Private.CoreLib's Resources/Strings.resx (dotnet/runtime release/11.0, commit
// bc123d0bd173) that the System.Globalization port uses: the English texts .NET prints. Generated from the
// SR.* names in this directory; see THIRD-PARTY-NOTICES.md.
#nullable enable annotations

using System;

namespace CsharpcCoreLib.Globalization
{
    // (repeated inside the namespace: they must win over global-namespace types of the program)
    using System;
    internal static class SR
    {
        public static string Format(string format, object? arg0) => string.Format(format, arg0);
        public static string Format(string format, object? arg0, object? arg1) => string.Format(format, arg0, arg1);
        public static string Format(string format, object? arg0, object? arg1, object? arg2) => string.Format(format, arg0, arg1, arg2);
        public static string Format(IFormatProvider? provider, string format, object? arg0) => string.Format(provider, format, arg0);
        public static string Format(IFormatProvider? provider, string format, object? arg0, object? arg1) => string.Format(provider, format, arg0, arg1);
        public static string Format(IFormatProvider? provider, string format, object? arg0, object? arg1, object? arg2) => string.Format(provider, format, arg0, arg1, arg2);

        public const string Arg_ArrayZeroError = "Array must not be of length zero.";
        public const string Arg_BinaryStyleNotSupported = "The number style AllowBinarySpecifier is not supported on floating point data types.";
        public const string Arg_ExternalException = "External component has thrown an exception.";
        public const string Arg_HexBinaryStylesNotSupported = "The number styles AllowHexSpecifier and AllowBinarySpecifier are not supported on the decimal type.";
        public const string Arg_InvalidHexBinaryStyle = "With the AllowHexSpecifier or AllowBinarySpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be AllowLeadingWhite and AllowTrailingWhite.";
        public const string Arg_InvalidHexFloatStyle = "With the AllowHexSpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be a subset of HexFloat (AllowLeadingWhite, AllowTrailingWhite, AllowLeadingSign, AllowDecimalPoint, and AllowExponent). AllowExponent is required when AllowHexSpecifier is specified.";
        public const string ArgumentNull_ArrayValue = "Found a null value within an array.";
        public const string ArgumentNull_String = "String reference not set to an instance of a String.";
        public const string ArgumentOutOfRange_AddValue = "Value to add was out of range.";
        public const string ArgumentOutOfRange_BadHourMinuteSecond = "Hour, Minute, and Second parameters describe an un-representable DateTime.";
        public const string ArgumentOutOfRange_BadYearMonthDay = "Year, Month, and Day parameters describe an un-representable DateTime.";
        public const string ArgumentOutOfRange_Bounds_Lower_Upper = "Argument must be between {0} and {1}.";
        public const string ArgumentOutOfRange_CalendarRange = "Specified time is not supported in this calendar. It should be between {0} (Gregorian date) and {1} (Gregorian date), inclusive.";
        public const string ArgumentOutOfRange_Count = "Count must be positive and count must refer to a location within the string/array/collection.";
        public const string ArgumentOutOfRange_Day = "Day must be between 1 and {0} for month {1}.";
        public const string ArgumentOutOfRange_Era = "Time value was out of era range.";
        public const string ArgumentOutOfRange_GetByteCountOverflow = "Too many characters. The resulting number of bytes is larger than what can be returned as an int.";
        public const string ArgumentOutOfRange_IndexMustBeLess = "Index was out of range. Must be non-negative and less than the size of the collection.";
        public const string ArgumentOutOfRange_IndexMustBeLessOrEqual = "Index was out of range. Must be non-negative and less than or equal to the size of the collection.";
        public const string ArgumentOutOfRange_InvalidEraValue = "Era value was not valid.";
        public const string ArgumentOutOfRange_Month = "Month must be between one and twelve.";
        public const string ArgumentOutOfRange_NeedNonNegNum = "Non-negative number required.";
        public const string ArgumentOutOfRange_OffsetLength = "Offset and length must refer to a position in the string.";
        public const string ArgumentOutOfRange_Range = "Valid values are between {0} and {1}, inclusive.";
        public const string Argument_BadFormatSpecifier = "Format specifier was invalid.";
        public const string Argument_CompareOptionOrdinal = "CompareOption.Ordinal cannot be used with other options.";
        public const string Argument_ConflictingDateTimeRoundtripStyles = "The DateTimeStyles value RoundtripKind cannot be used with the values AssumeLocal, AssumeUniversal or AdjustToUniversal.";
        public const string Argument_ConflictingDateTimeStyles = "The DateTimeStyles values AssumeLocal and AssumeUniversal cannot be used together.";
        public const string Argument_CultureIetfNotSupported = "Culture IETF Name {0} is not a recognized IETF name.";
        public const string Argument_CultureInvalidIdentifier = "{0} is an invalid culture identifier.";
        public const string Argument_CultureIsNeutral = "Culture ID {0} (0x{0:X4}) is a neutral culture; a region cannot be created from it.";
        public const string Argument_CultureNotSupported = "Culture is not supported.";
        public const string Argument_CultureNotSupportedInInvariantMode = "Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information.";
        public const string Argument_CustomCultureCannotBePassedByNumber = "Customized cultures cannot be passed by LCID, only by name.";
        public const string Argument_DestinationTooShort = "Destination is too short.";
        public const string Argument_InvalidArrayLength = "Length of the array must be {0}.";
        public const string Argument_InvalidCalendar = "Not a valid calendar for the given culture.";
        public const string Argument_InvalidCultureName = "Culture name '{0}' is not supported.";
        public const string Argument_InvalidDateStyles = "The only allowed values for the styles are AllowWhiteSpaces, AllowTrailingWhite, AllowLeadingWhite, and AllowInnerWhite.";
        public const string Argument_InvalidDateTimeStyles = "An undefined DateTimeStyles value is being used.";
        public const string Argument_InvalidDigitSubstitution = "The DigitSubstitution property must be of a valid member of the DigitShapes enumeration. Valid entries include Context, NativeNational or None.";
        public const string Argument_InvalidFlag = "Value of flags is invalid.";
        public const string Argument_InvalidGroupSize = "Every element in the value array should be between one and nine, except for the last element, which can be zero.";
        public const string Argument_InvalidId = "The specified ID parameter '{0}' is not supported.";
        public const string Argument_InvalidNativeDigitCount = "The NativeDigits array must contain exactly ten members.";
        public const string Argument_InvalidNativeDigitValue = "Each member of the NativeDigits array must be a single text element (one or more UTF-16 code points) with a Unicode Nd (Number, Decimal Digit) property indicating it is a digit.";
        public const string Argument_InvalidNeutralRegionName = "The region name {0} should not correspond to neutral culture; a specific culture name is required.";
        public const string Argument_InvalidNumberStyles = "An undefined NumberStyles value is being used.";
        public const string Argument_InvalidPredefinedCultureName = "Culture name '{0}' is not a predefined culture.";
        public const string Argument_InvalidResourceCultureName = "The given culture name '{0}' cannot be used to locate a resource file. Resource filenames must consist of only letters, numbers, hyphens or underscores.";
        public const string Argument_NoEra = "No Era was supplied.";
        public const string Argument_NoRegionInvariantCulture = "There is no region associated with the Invariant Culture (Culture ID: 0x7F).";
        public const string Argument_OneOfCulturesNotSupported = "Culture name {0} or {1} is not supported.";
        public const string Argument_OnlyMscorlib = "Only mscorlib's assembly is valid.";
        public const string Argument_ResultCalendarRange = "The result is out of the supported range for this calendar. The result should be between {0} (Gregorian date) and {1} (Gregorian date), inclusive.";
        public const string Format_BadDateOnly = "String '{0}' was not recognized as a valid DateOnly.";
        public const string Format_BadDatePattern = "Could not determine the order of year, month, and date from '{0}'.";
        public const string Format_BadDateTime = "String '{0}' was not recognized as a valid DateTime.";
        public const string Format_BadDateTimeCalendar = "The DateTime represented by the string '{0}' is not supported in calendar '{1}'.";
        public const string Format_BadDayOfWeek = "String '{0}' was not recognized as a valid DateTime because the day of week was incorrect.";
        public const string Format_BadFormatSpecifier = "Format specifier '{0}' was invalid.";
        public const string Format_BadQuote = "Cannot find a matching quote character for the character '{0}'.";
        public const string Format_BadTimeOnly = "String '{0}' was not recognized as a valid TimeOnly.";
        public const string Format_DateOutOfRange = "The DateTime represented by the string '{0}' is out of range.";
        public const string Format_DateTimeOnlyContainsNoneDateParts = "String '{0}' contains parts which are not specific to the {1}.";
        public const string Format_InvalidString = "Input string was not in a correct format.";
        public const string Format_MissingIncompleteDate = "There must be at least a partial date with a year present in the input string '{0}'.";
        public const string Format_NoFormatSpecifier = "No format specifiers were provided.";
        public const string Format_OffsetOutOfRange = "The time zone offset of string '{0}' must be within plus or minus 14 hours.";
        public const string Format_RepeatDateTimePattern = "DateTime pattern '{0}' appears more than once with different values.";
        public const string Format_UTCOutOfRange = "The UTC representation of the date '{0}' falls outside the year range 1-9999.";
        public const string Format_UnknownDateTimeWord = "The string '{0}' was not recognized as a valid DateTime. There is an unknown word starting at index '{1}'.";
        public const string InvalidOperation_DateTimeParsing = "Internal Error in DateTime and Calendar operations.";
        public const string InvalidOperation_ReadOnly = "Instance is read-only.";
    }
}
