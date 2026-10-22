# Conformance results (2026-10-22, csharpc -O2)

**168/195 programs pass (86.2%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 3 | 4 | 75.0% |
| bcl | 3 | 12 | 25.0% |
| classes | 15 | 15 | 100.0% |
| collections | 7 | 8 | 87.5% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 17 | 20 | 85.0% |
| formatting | 5 | 5 | 100.0% |
| generics | 7 | 8 | 87.5% |
| interfaces | 9 | 9 | 100.0% |
| iterators | 4 | 4 | 100.0% |
| lambdas | 6 | 6 | 100.0% |
| lexical | 10 | 10 | 100.0% |
| linq | 6 | 6 | 100.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 18 | 18 | 100.0% |
| patterns | 5 | 8 | 62.5% |
| programs | 5 | 6 | 83.3% |
| statements | 12 | 12 | 100.0% |
| types | 17 | 25 | 68.0% |
| **total** | **168** | **195** | **86.2%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 4 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 2 | 2 | missing library member: System.Reflection.MemberInfo.Name | expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 3 | 1 | runtime abort: Unhandled exception. System.InvalidOperationException: async boom | async/003_valuetask_exceptions |
| 4 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 5 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 6 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 7 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 8 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 9 | 1 | missing library member: System.Convert.ToString | bcl/009_char_parse_convert |
| 10 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 11 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 12 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 13 | 1 | missing library member: System.Array.Copy | collections/008_array_methods |
| 14 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 15 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 16 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type 'System.Collections. | generics/006_variance |
| 17 | 1 | virtual call to 'Order.Equals(Order?)' | patterns/003_property_patterns |
| 18 | 1 | virtual call to 'Order.PrintMembers(System.Text.StringBuilder)' | patterns/003_property_patterns |
| 19 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 20 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 21 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 22 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 23 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 24 | 1 | field '*' of struct '*' (its layout could not be built) | types/018_readonly_struct |
| 25 | 1 | missing library member: System.Type.FullName | types/021_object_methods |
| 26 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
