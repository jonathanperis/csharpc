# Conformance results (2026-10-22, csharpc -O2)

**91/195 programs pass (46.7%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 2 | 12 | 16.7% |
| classes | 14 | 15 | 93.3% |
| collections | 0 | 8 | 0.0% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 7 | 20 | 35.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 0 | 8 | 0.0% |
| interfaces | 0 | 9 | 0.0% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 3 | 4 | 75.0% |
| members | 12 | 18 | 66.7% |
| patterns | 3 | 8 | 37.5% |
| programs | 2 | 6 | 33.3% |
| statements | 8 | 12 | 66.7% |
| types | 10 | 25 | 40.0% |
| **total** | **91** | **195** | **46.7%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 16 | interface implementation on '*' | collections/006_list_of_objects, expressions/012_target_typed_new, generics/007_generic_interface |
| 2 | 7 | interface '*' | generics/007_generic_interface, interfaces/001_basic, interfaces/002_multiple |
| 3 | 6 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 4 | 6 | generic local function '*' | expressions/008_default_literal, generics/002_generic_methods, generics/003_constraints |
| 5 | 6 | generic method '*' | expressions/008_default_literal, generics/002_generic_methods, generics/003_constraints |
| 6 | 5 | foreach over 'System.Collections.Generic.IEnumerable<>' (only arrays so far) | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 7 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 8 | 4 | operation YieldReturn | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 9 | 4 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let, programs/001_quicksort |
| 10 | 3 | missing library member: int.CompareTo | collections/001_list, collections/006_list_of_objects, interfaces/007_icomparable |
| 11 | 3 | generic type parameter '*' | generics/001_generic_class, generics/004_generic_struct, generics/005_nested_instantiations |
| 12 | 2 | struct 'System.Span<>' | bcl/004_span_memory, types/019_ref_struct_span |
| 13 | 2 | creating instances of 'System.Collections.Generic.Dictionary<,>' | collections/002_dictionary, programs/003_bank_simulation |
| 14 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 15 | 2 | missing library member: System.Reflection.MemberInfo.Name | expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 16 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 17 | 2 | record struct '*' | expressions/016_user_defined_conversions_chain, types/016_record_struct |
| 18 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 19 | 2 | generic type '*' | generics/001_generic_class, generics/008_static_generic_cache |
| 20 | 2 | struct 'System.ReadOnlySpan<>' | lexical/007_utf8_literals, members/015_params_span |
| 21 | 2 | internal compiler error: invalid LLVM IR | members/011_partial_methods, members/012_partial_properties |
| 22 | 2 | pattern RecursivePattern | patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 23 | 2 | System.Decimal | programs/003_bank_simulation, types/003_decimal |
| 24 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 25 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 26 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 27 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 28 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 29 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 30 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 31 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 32 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 33 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type '*' to type 'Syste | classes/011_is_as_cast |
| 34 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 35 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 36 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 37 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 38 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 39 | 1 | operator Add on reference types | delegates/003_multicast |
| 40 | 1 | operation EventAssignment | delegates/004_events |
| 41 | 1 | operation EventReference | delegates/004_events |
| 42 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 43 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 44 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 45 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 46 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 47 | 1 | generic struct '*' | generics/004_generic_struct |
| 48 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 49 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 50 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 51 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 52 | 1 | foreach over 'System.Linq.IOrderedEnumerable<System.Linq.IGrouping<,,>>' (only arrays so far) | linq/003_groupby_orderby |
| 53 | 1 | missing library member: System.Linq.IGrouping<,,>.Key | linq/003_groupby_orderby |
| 54 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 55 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 56 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 57 | 1 | collection expression of type 'System.ReadOnlySpan<>' | members/015_params_span |
| 58 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 59 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 60 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 61 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 62 | 1 | missing library member: System.Linq.IGrouping<,>.Key | programs/005_word_frequency_linq |
| 63 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 64 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 65 | 1 | operation None | statements/009_fixed |
| 66 | 1 | missing library member: double.IsNaN | types/002_floating |
| 67 | 1 | missing library member: nint.Size | types/004_nint |
| 68 | 1 | missing library member: char.CompareTo | types/005_bool_char |
| 69 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 70 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 71 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 72 | 1 | compiler crash: System.Collections.Generic.KeyNotFoundException | types/018_readonly_struct |
| 73 | 1 | missing library member: System.Type.FullName | types/021_object_methods |
| 74 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
