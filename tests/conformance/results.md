# Conformance results (2026-10-22, csharpc -O2)

**100/195 programs pass (51.3%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 8 | 12 | 66.7% |
| classes | 14 | 15 | 93.3% |
| collections | 1 | 8 | 12.5% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 8 | 20 | 40.0% |
| formatting | 4 | 5 | 80.0% |
| generics | 0 | 8 | 0.0% |
| interfaces | 0 | 9 | 0.0% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 3 | 4 | 75.0% |
| members | 14 | 18 | 77.8% |
| patterns | 3 | 8 | 37.5% |
| programs | 3 | 6 | 50.0% |
| statements | 7 | 12 | 58.3% |
| types | 16 | 25 | 64.0% |
| **total** | **100** | **195** | **51.3%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 16 | interface implementation on '*' | collections/006_list_of_objects, expressions/012_target_typed_new, generics/007_generic_interface |
| 2 | 11 | try/catch (exception handling) | collections/002_dictionary, exceptions/001_try_catch, exceptions/002_finally |
| 3 | 7 | interface '*' | generics/007_generic_interface, interfaces/001_basic, interfaces/002_multiple |
| 4 | 6 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 5 | 6 | generic local function '*' | expressions/008_default_literal, generics/002_generic_methods, generics/003_constraints |
| 6 | 6 | generic method '*' | expressions/008_default_literal, generics/002_generic_methods, generics/003_constraints |
| 7 | 5 | foreach over 'System.Collections.Generic.IEnumerable<>' (only arrays so far) | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 8 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 9 | 4 | operation YieldReturn | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 10 | 3 | generic type parameter '*' | generics/001_generic_class, generics/004_generic_struct, generics/005_nested_instantiations |
| 11 | 2 | struct 'System.Span<>' | bcl/004_span_memory, types/019_ref_struct_span |
| 12 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 13 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 14 | 2 | record struct '*' | expressions/016_user_defined_conversions_chain, types/016_record_struct |
| 15 | 2 | generic type '*' | generics/001_generic_class, generics/008_static_generic_cache |
| 16 | 2 | struct 'System.ReadOnlySpan<>' | lexical/007_utf8_literals, members/015_params_span |
| 17 | 2 | pattern RecursivePattern | patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 18 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 19 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 20 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 21 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type '*' to type 'Syste | classes/011_is_as_cast |
| 22 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 23 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 24 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 25 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 26 | 1 | operator Add on reference types | delegates/003_multicast |
| 27 | 1 | operation EventAssignment | delegates/004_events |
| 28 | 1 | operation EventReference | delegates/004_events |
| 29 | 1 | deriving from library class 'System.Exception' | exceptions/003_custom |
| 30 | 1 | creating instances of 'System.ArgumentOutOfRangeException' | exceptions/009_exception_tostring |
| 31 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 32 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 33 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 34 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 35 | 1 | wrong output | formatting/003_custom_formats |
| 36 | 1 | generic struct '*' | generics/004_generic_struct |
| 37 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 38 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 39 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 40 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 41 | 1 | foreach over 'System.Linq.IOrderedEnumerable<System.Linq.IGrouping<,,>>' (only arrays so far) | linq/003_groupby_orderby |
| 42 | 1 | missing library member: System.Linq.IGrouping<,,>.Key | linq/003_groupby_orderby |
| 43 | 1 | missing library member: System.Linq.Enumerable.Distinct | linq/005_aggregate_distinct_zip |
| 44 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 45 | 1 | missing library member: System.Linq.Enumerable.SelectMany | linq/006_selectmany_let |
| 46 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 47 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 48 | 1 | collection expression of type 'System.ReadOnlySpan<>' | members/015_params_span |
| 49 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 50 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 51 | 1 | missing library member: System.Collections.Generic.Dictionary<,>.this[] | programs/003_bank_simulation |
| 52 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 53 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 54 | 1 | missing library member: System.Linq.IGrouping<,>.Key | programs/005_word_frequency_linq |
| 55 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 56 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 57 | 1 | operation None | statements/009_fixed |
| 58 | 1 | missing library member: nint.Size | types/004_nint |
| 59 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 60 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 61 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 62 | 1 | missing library member: object.ReferenceEquals | types/021_object_methods |
| 63 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
