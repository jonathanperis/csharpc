# Conformance results (2026-10-22, csharpc -O2)

**127/195 programs pass (65.1%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 8 | 12 | 66.7% |
| classes | 15 | 15 | 100.0% |
| collections | 1 | 8 | 12.5% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 11 | 20 | 55.0% |
| formatting | 5 | 5 | 100.0% |
| generics | 5 | 8 | 62.5% |
| interfaces | 6 | 9 | 66.7% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 15 | 18 | 83.3% |
| patterns | 6 | 8 | 75.0% |
| programs | 4 | 6 | 66.7% |
| statements | 8 | 12 | 66.7% |
| types | 20 | 25 | 80.0% |
| **total** | **127** | **195** | **65.1%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 11 | try/catch (exception handling) | collections/002_dictionary, exceptions/001_try_catch, exceptions/002_finally |
| 2 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 3 | 5 | operation YieldReturn | interfaces/009_ienumerable_impl, iterators/001_yield_return, iterators/002_yield_break_lazy |
| 4 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 5 | 2 | struct 'System.Span<>' | bcl/004_span_memory, types/019_ref_struct_span |
| 6 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 7 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 8 | 2 | struct 'System.ReadOnlySpan<>' | lexical/007_utf8_literals, members/015_params_span |
| 9 | 1 | missing library member: double.Round | bcl/003_math |
| 10 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 11 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 12 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 13 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 14 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 15 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 16 | 1 | operator Add on reference types | delegates/003_multicast |
| 17 | 1 | operation EventAssignment | delegates/004_events |
| 18 | 1 | operation EventReference | delegates/004_events |
| 19 | 1 | deriving from library class 'System.Exception' | exceptions/003_custom |
| 20 | 1 | creating instances of 'System.ArgumentOutOfRangeException' | exceptions/009_exception_tostring |
| 21 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 22 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 23 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 24 | 1 | generic struct '*' | generics/004_generic_struct |
| 25 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 26 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 27 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 28 | 1 | Array.Sort on '*' (needs IComparable dispatch) | interfaces/007_icomparable |
| 29 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 30 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 31 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 32 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 33 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 34 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 35 | 1 | missing library member: System.Linq.Enumerable.Distinct | linq/005_aggregate_distinct_zip |
| 36 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 37 | 1 | missing library member: System.Linq.Enumerable.SelectMany | linq/006_selectmany_let |
| 38 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 39 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 40 | 1 | collection expression of type 'System.ReadOnlySpan<>' | members/015_params_span |
| 41 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 42 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 43 | 1 | missing library member: System.Collections.Generic.Dictionary<,>.this[] | programs/003_bank_simulation |
| 44 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 45 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 46 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 47 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 48 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 49 | 1 | operation None | statements/009_fixed |
| 50 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 51 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 52 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 53 | 1 | missing library member: string.Remove | types/022_string_basics |
