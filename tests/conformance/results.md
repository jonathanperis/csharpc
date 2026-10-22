# Conformance results (2026-10-22, csharpc -O2)

**161/195 programs pass (82.6%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 3 | 4 | 75.0% |
| bcl | 3 | 12 | 25.0% |
| classes | 15 | 15 | 100.0% |
| collections | 7 | 8 | 87.5% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 15 | 20 | 75.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 7 | 8 | 87.5% |
| interfaces | 8 | 9 | 88.9% |
| iterators | 4 | 4 | 100.0% |
| lambdas | 6 | 6 | 100.0% |
| lexical | 10 | 10 | 100.0% |
| linq | 5 | 6 | 83.3% |
| localfunctions | 4 | 4 | 100.0% |
| members | 17 | 18 | 94.4% |
| patterns | 5 | 8 | 62.5% |
| programs | 5 | 6 | 83.3% |
| statements | 12 | 12 | 100.0% |
| types | 17 | 25 | 68.0% |
| **total** | **161** | **195** | **82.6%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 4 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 2 | 2 | missing library member: System.Reflection.MemberInfo.Name | expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 3 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 4 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 5 | 1 | runtime abort: Unhandled exception. System.InvalidOperationException: async boom | async/003_valuetask_exceptions |
| 6 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 7 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 8 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 9 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 10 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 11 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 12 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 13 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 14 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 15 | 1 | missing library member: System.Array.Copy | collections/008_array_methods |
| 16 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 17 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 18 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 19 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type 'System.Collections. | generics/006_variance |
| 20 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 21 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 22 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 23 | 1 | virtual call to 'Order.Equals(Order?)' | patterns/003_property_patterns |
| 24 | 1 | virtual call to 'Order.PrintMembers(System.Text.StringBuilder)' | patterns/003_property_patterns |
| 25 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 26 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 27 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 28 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 29 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 30 | 1 | field '*' of struct '*' (its layout could not be built) | types/018_readonly_struct |
| 31 | 1 | missing library member: System.Type.FullName | types/021_object_methods |
| 32 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
