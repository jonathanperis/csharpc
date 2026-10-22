# Conformance results (2026-10-22, csharpc -O2)

**109/195 programs pass (55.9%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 2 | 12 | 16.7% |
| classes | 15 | 15 | 100.0% |
| collections | 0 | 8 | 0.0% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 8 | 20 | 40.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 4 | 8 | 50.0% |
| interfaces | 6 | 9 | 66.7% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 15 | 18 | 83.3% |
| patterns | 3 | 8 | 37.5% |
| programs | 2 | 6 | 33.3% |
| statements | 9 | 12 | 75.0% |
| types | 11 | 25 | 44.0% |
| **total** | **109** | **195** | **55.9%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 2 | 5 | operation YieldReturn | interfaces/009_ienumerable_impl, iterators/001_yield_return, iterators/002_yield_break_lazy |
| 3 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 4 | 4 | record '*' | collections/006_list_of_objects, expressions/012_target_typed_new, patterns/004_positional_patterns |
| 5 | 4 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let, programs/001_quicksort |
| 6 | 3 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 7 | 3 | pattern RecursivePattern | patterns/004_positional_patterns, patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 8 | 2 | struct 'System.Span<>' | bcl/004_span_memory, types/019_ref_struct_span |
| 9 | 2 | creating instances of 'System.Collections.Generic.Dictionary<,>' | collections/002_dictionary, programs/003_bank_simulation |
| 10 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 11 | 2 | missing library member: System.Reflection.MemberInfo.Name | expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 12 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 13 | 2 | record struct '*' | expressions/016_user_defined_conversions_chain, types/016_record_struct |
| 14 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 15 | 2 | struct 'System.ReadOnlySpan<>' | lexical/007_utf8_literals, members/015_params_span |
| 16 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 17 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 18 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 19 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 20 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 21 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 22 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 23 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 24 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 25 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 26 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 27 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 28 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 29 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 30 | 1 | operator Add on reference types | delegates/003_multicast |
| 31 | 1 | operation EventAssignment | delegates/004_events |
| 32 | 1 | operation EventReference | delegates/004_events |
| 33 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 34 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 35 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 36 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 37 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 38 | 1 | missing library member: System.Array.Resize | generics/001_generic_class |
| 39 | 1 | generic struct '*' | generics/004_generic_struct |
| 40 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 41 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 42 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 43 | 1 | missing library member: System.Array.Sort | interfaces/007_icomparable |
| 44 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 45 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 46 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 47 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 48 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 49 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 50 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 51 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 52 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 53 | 1 | collection expression of type 'System.ReadOnlySpan<>' | members/015_params_span |
| 54 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 55 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 56 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 57 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 58 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 59 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 60 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 61 | 1 | operation None | statements/009_fixed |
| 62 | 1 | missing library member: double.IsNaN | types/002_floating |
| 63 | 1 | missing library member: nint.Size | types/004_nint |
| 64 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 65 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 66 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 67 | 1 | deconstruction of this value (user Deconstruct methods are not supported yet) | types/015_record_class |
| 68 | 1 | compiler crash: System.Collections.Generic.KeyNotFoundException | types/018_readonly_struct |
| 69 | 1 | missing library member: System.Type.FullName | types/021_object_methods |
| 70 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
