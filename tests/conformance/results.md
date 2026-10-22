# Conformance results (2026-10-22, csharpc -O2)

**119/195 programs pass (61.0%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 3 | 12 | 25.0% |
| classes | 14 | 15 | 93.3% |
| collections | 0 | 8 | 0.0% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 11 | 20 | 55.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 4 | 8 | 50.0% |
| interfaces | 6 | 9 | 66.7% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 10 | 10 | 100.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 17 | 18 | 94.4% |
| patterns | 5 | 8 | 62.5% |
| programs | 4 | 6 | 66.7% |
| statements | 11 | 12 | 91.7% |
| types | 15 | 25 | 60.0% |
| **total** | **119** | **195** | **61.0%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 11 | try/catch (exception handling) | collections/002_dictionary, exceptions/001_try_catch, exceptions/002_finally |
| 2 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 3 | 5 | operation YieldReturn | interfaces/009_ienumerable_impl, iterators/001_yield_return, iterators/002_yield_break_lazy |
| 4 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 5 | 4 | missing library member: System.Reflection.MemberInfo.Name | classes/011_is_as_cast, expressions/007_nameof_typeof_sizeof, types/021_object_methods |
| 6 | 4 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 7 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 8 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 9 | 2 | missing library member: System.Array.Sort | interfaces/007_icomparable, types/010_arrays_1d |
| 10 | 2 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let |
| 11 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 12 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 13 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 14 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 15 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 16 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 17 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 18 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 19 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 20 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 21 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 22 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 23 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 24 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 25 | 1 | deriving from library class 'System.Exception' | exceptions/003_custom |
| 26 | 1 | creating instances of 'System.ArgumentOutOfRangeException' | exceptions/009_exception_tostring |
| 27 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 28 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 29 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 30 | 1 | missing library member: System.Array.Resize | generics/001_generic_class |
| 31 | 1 | generic struct '*' | generics/004_generic_struct |
| 32 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 33 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 34 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 35 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 36 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 37 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 38 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 39 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 40 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 41 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 42 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 43 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 44 | 1 | virtual call to 'Order.Equals(Order?)' | patterns/003_property_patterns |
| 45 | 1 | virtual call to 'Order.PrintMembers(System.Text.StringBuilder)' | patterns/003_property_patterns |
| 46 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 47 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 48 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 49 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 50 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 51 | 1 | missing library member: double.IsNaN | types/002_floating |
| 52 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 53 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 54 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 55 | 1 | field '*' of struct '*' (its layout could not be built) | types/018_readonly_struct |
| 56 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
