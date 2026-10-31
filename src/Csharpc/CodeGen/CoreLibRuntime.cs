namespace Csharpc.CodeGen;

/// <summary>
/// Former C runtime (src/runtime) entry points that are now implemented in C# in the corelib
/// (src/Csharpc/corelib; ADR 0008). Codegen paths that still ask for the C symbol by name are
/// redirected here, so moving a function to C# never requires touching its call sites.
/// Argument and return types must match the C signature (string/array = ptr, int = i32, char = i16, bool = i1).
/// </summary>
internal static class CoreLibRuntime
{
    private static readonly Dictionary<string, (string Type, string Method)> s_moved = new()
    {
        ["csrt_string_join"] = ("StringOps", "Join"),
        ["csrt_string_substring"] = ("StringOps", "Slice"),
        ["csrt_string_new_repeat"] = ("StringOps", "CreateRepeated"),
        ["csrt_string_new_from_chars"] = ("StringOps", "CreateFromChars"),
        ["csrt_string_compare_ordinal"] = ("StringOps", "CompareOrdinal"),
        // culture-sensitive comparison (Globalization/CultureStrings.cs)
        ["csrt_string_compare_culture"] = ("CultureStrings", "CompareCulture"),
        ["csrt_string_compare_with"] = ("CultureStrings", "CompareWith"),
        // System.Text.StringBuilder (records' and anonymous types' synthesized ToString use these)
        ["csrt_sb_new"] = ("StringBuilderImpl", "RtNew"),
        ["csrt_sb_append_string"] = ("StringBuilderImpl", "RtAppend"),
        ["csrt_sb_to_string"] = ("StringBuilderImpl", "RtToString"),
        ["csrt_sb_append_aligned"] = ("StringBuilderImpl", "RtAppendAligned"),
        // integer -> string (ToString(), interpolation, concatenation)
        ["csrt_string_from_i32"] = ("Number", "Int32ToDecStr"),
        ["csrt_string_from_i64"] = ("Number", "Int64ToDecStr"),
        ["csrt_string_from_u32"] = ("Number", "UInt32ToDecStr"),
        ["csrt_string_from_u64"] = ("Number", "UInt64ToDecStr"),
        // floating point and format strings (Number.Formatting.cs)
        ["csrt_string_from_f64"] = ("Number", "DoubleToString"),
        ["csrt_string_from_f32"] = ("Number", "SingleToString"),
        ["csrt_console_write_f64"] = ("Number", "ConsoleWriteDouble"),
        ["csrt_console_write_f32"] = ("Number", "ConsoleWriteSingle"),
        ["csrt_format_i64"] = ("Number", "FormatInt64"),
        ["csrt_format_u64"] = ("Number", "FormatUInt64"),
        ["csrt_format_f64"] = ("Number", "FormatDouble"),
        ["csrt_string_align"] = ("Number", "Align"),
        ["csrt_format_object"] = ("Number", "FormatObject"),
        ["csrt_string_format"] = ("Number", "FormatComposite"),
        ["csrt_format_i128"] = ("Number", "FormatInt128"),
        // parsing and rounding (Number.Parsing.cs)
        ["csrt_int32_parse"] = ("Number", "ParseInt32"),
        ["csrt_int64_parse"] = ("Number", "ParseInt64"),
        ["csrt_double_parse"] = ("Number", "ParseDouble"),
        ["csrt_int32_try_parse"] = ("Number", "TryParseInt32"),
        ["csrt_int64_try_parse"] = ("Number", "TryParseInt64"),
        ["csrt_double_try_parse"] = ("Number", "TryParseDouble"),
        ["csrt_parse_i64_styled"] = ("Number", "ParseInt64Styled"),
        ["csrt_parse_f64_styled"] = ("Number", "ParseDoubleStyled"),
        ["csrt_parse_f64_styled_or_throw"] = ("Number", "ParseDoubleStyledOrThrow"),
        ["csrt_math_round_digits"] = ("Number", "RoundDigits"),
        ["csrt_math_round_mode"] = ("Number", "RoundMode"),
        // System.Decimal operations taking decimals by address (DecimalOps.cs); see also FunctionCodeGen.DecimalCall
        ["csrt_decimal_compare"] = ("DecimalOps", "CompareAt"),
        ["csrt_decimal_get_hash_code"] = ("DecimalOps", "HashAt"),
        ["csrt_decimal_to_f64"] = ("DecimalOps", "ToDoubleAt"),
        ["csrt_decimal_to_f32"] = ("DecimalOps", "ToSingleAt"),
        ["csrt_decimal_to_integer"] = ("DecimalOps", "ToIntegerAt"),
        ["csrt_decimal_try_parse"] = ("DecimalOps", "TryParse"),
        // System.Numerics.BigInteger (BigIntegerOps.cs)
        ["csrt_bigint_abs"] = ("BigIntegerOps", "Abs"),
        ["csrt_bigint_add"] = ("BigIntegerOps", "Add"),
        ["csrt_bigint_compare"] = ("BigIntegerOps", "Compare"),
        ["csrt_bigint_div"] = ("BigIntegerOps", "Divide"),
        ["csrt_bigint_divrem"] = ("BigIntegerOps", "DivRem"),
        ["csrt_bigint_format"] = ("BigIntegerOps", "Format"),
        ["csrt_bigint_from_f64"] = ("BigIntegerOps", "FromDouble"),
        ["csrt_bigint_from_i64"] = ("BigIntegerOps", "FromInt64"),
        ["csrt_bigint_from_u64"] = ("BigIntegerOps", "FromUInt64"),
        ["csrt_bigint_gcd"] = ("BigIntegerOps", "GreatestCommonDivisor"),
        ["csrt_bigint_get_hash_code"] = ("BigIntegerOps", "Hash"),
        ["csrt_bigint_is_even"] = ("BigIntegerOps", "IsEven"),
        ["csrt_bigint_mod_pow"] = ("BigIntegerOps", "ModPow"),
        ["csrt_bigint_mul"] = ("BigIntegerOps", "Multiply"),
        ["csrt_bigint_negate"] = ("BigIntegerOps", "Negate"),
        ["csrt_bigint_parse"] = ("BigIntegerOps", "Parse"),
        ["csrt_bigint_pow"] = ("BigIntegerOps", "Pow"),
        ["csrt_bigint_rem"] = ("BigIntegerOps", "Remainder"),
        ["csrt_bigint_sign"] = ("BigIntegerOps", "Sign"),
        ["csrt_bigint_sub"] = ("BigIntegerOps", "Subtract"),
        ["csrt_bigint_to_f64"] = ("BigIntegerOps", "ToDouble"),
        ["csrt_bigint_to_integer"] = ("BigIntegerOps", "ToInteger"),
        ["csrt_bigint_to_small_integer"] = ("BigIntegerOps", "ToSmallInteger"),
        ["csrt_bigint_to_string"] = ("BigIntegerOps", "ToDecimalString"),
        ["csrt_bigint_try_parse"] = ("BigIntegerOps", "TryParse"),
        // System.TimeSpan / System.DateTime (DateTimeOps.cs)
        ["csrt_timespan_add"] = ("DateTimeOps", "TimeSpanAdd"),
        ["csrt_timespan_sub"] = ("DateTimeOps", "TimeSpanSubtract"),
        ["csrt_timespan_negate"] = ("DateTimeOps", "TimeSpanNegate"),
        ["csrt_timespan_duration"] = ("DateTimeOps", "TimeSpanDuration"),
        ["csrt_timespan_from_double"] = ("DateTimeOps", "TimeSpanFromDouble"),
        ["csrt_timespan_from_units"] = ("DateTimeOps", "TimeSpanFromUnits"),
        ["csrt_timespan_multiply"] = ("DateTimeOps", "TimeSpanMultiply"),
        ["csrt_timespan_divide"] = ("DateTimeOps", "TimeSpanDivide"),
        ["csrt_timespan_to_string"] = ("DateTimeOps", "TimeSpanToString"),
        ["csrt_timespan_format"] = ("DateTimeOps", "TimeSpanFormat"),
        ["csrt_datetime_is_leap_year"] = ("DateTimeOps", "IsLeapYear"),
        ["csrt_datetime_days_in_month"] = ("DateTimeOps", "DaysInMonth"),
        ["csrt_datetime_new"] = ("DateTimeOps", "New"),
        ["csrt_datetime_from_ticks"] = ("DateTimeOps", "FromTicks"),
        ["csrt_datetime_part"] = ("DateTimeOps", "Part"),
        ["csrt_datetime_add_ticks"] = ("DateTimeOps", "AddTicks"),
        ["csrt_datetime_add_units"] = ("DateTimeOps", "AddUnits"),
        ["csrt_datetime_add_months"] = ("DateTimeOps", "AddMonths"),
        ["csrt_datetime_add_years"] = ("DateTimeOps", "AddYears"),
        ["csrt_datetime_to_string"] = ("DateTimeOps", "DateTimeToString"),
        ["csrt_datetime_format"] = ("DateTimeOps", "Format"),
    };

    public static bool TryGet(string runtimeFunction, out (string Type, string Method) target) =>
        s_moved.TryGetValue(runtimeFunction, out target);
}
