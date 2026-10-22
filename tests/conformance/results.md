# Conformance results (2026-10-22, csharpc -O2)

**137/195 programs pass (70.3%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 3 | 4 | 75.0% |
| bcl | 3 | 12 | 25.0% |
| classes | 15 | 15 | 100.0% |
| collections | 0 | 8 | 0.0% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 12 | 20 | 60.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 4 | 8 | 50.0% |
| interfaces | 7 | 9 | 77.8% |
| iterators | 3 | 4 | 75.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 10 | 10 | 100.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 17 | 18 | 94.4% |
| patterns | 5 | 8 | 62.5% |
| programs | 4 | 6 | 66.7% |
| statements | 12 | 12 | 100.0% |
| types | 15 | 25 | 60.0% |
| **total** | **137** | **195** | **70.3%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 2 | 4 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 3 | 2 | creating instances of 'System.Collections.Generic.Dictionary<,>' | collections/002_dictionary, programs/003_bank_simulation |
| 4 | 2 | missing library member: System.Reflection.MemberInfo.Name | expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 5 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 6 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 7 | 2 | missing library member: System.Array.Sort | interfaces/007_icomparable, types/010_arrays_1d |
| 8 | 2 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let |
| 9 | 1 | runtime abort: Unhandled exception. System.InvalidOperationException: async boom | async/003_valuetask_exceptions |
| 10 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 11 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 12 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 13 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 14 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 15 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 16 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 17 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 18 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 19 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 20 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 21 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 22 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 23 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 24 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 25 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 26 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 27 | 1 | missing library member: System.Array.Resize | generics/001_generic_class |
| 28 | 1 | generic struct '*' | generics/004_generic_struct |
| 29 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 30 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 31 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 32 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 33 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 34 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 35 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 36 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 37 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 38 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 39 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 40 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 41 | 1 | virtual call to 'Order.Equals(Order?)' | patterns/003_property_patterns |
| 42 | 1 | virtual call to 'Order.PrintMembers(System.Text.StringBuilder)' | patterns/003_property_patterns |
| 43 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 44 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 45 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 46 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 47 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 48 | 1 | missing library member: double.IsNaN | types/002_floating |
| 49 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 50 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 51 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 52 | 1 | field '*' of struct '*' (its layout could not be built) | types/018_readonly_struct |
| 53 | 1 | missing library member: System.Type.FullName | types/021_object_methods |
| 54 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
