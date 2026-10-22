# Conformance results (2026-10-22, csharpc -O2)

**159/195 programs pass (81.5%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 3 | 4 | 75.0% |
| bcl | 11 | 12 | 91.7% |
| classes | 15 | 15 | 100.0% |
| collections | 1 | 8 | 12.5% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 14 | 20 | 70.0% |
| formatting | 5 | 5 | 100.0% |
| generics | 5 | 8 | 62.5% |
| interfaces | 8 | 9 | 88.9% |
| iterators | 3 | 4 | 75.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 10 | 10 | 100.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 17 | 18 | 94.4% |
| patterns | 6 | 8 | 75.0% |
| programs | 4 | 6 | 66.7% |
| statements | 12 | 12 | 100.0% |
| types | 21 | 25 | 84.0% |
| **total** | **159** | **195** | **81.5%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 7 | creating instances of 'System.Collections.Generic.List<>' | collections/001_list, collections/006_list_of_objects, expressions/004_null_coalescing_assign |
| 2 | 2 | creating instances of 'System.Collections.Generic.Dictionary<,>' | collections/002_dictionary, programs/003_bank_simulation |
| 3 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 4 | 1 | runtime abort: Unhandled exception. System.InvalidOperationException: async boom | async/003_valuetask_exceptions |
| 5 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 6 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 7 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 8 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 9 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 10 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 11 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 12 | 1 | generic struct '*' | generics/004_generic_struct |
| 13 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 14 | 1 | missing library member: System.Collections.Generic.List<>.Count | generics/007_generic_interface |
| 15 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 16 | 1 | missing library member: System.Linq.Enumerable.Take | iterators/004_fibonacci |
| 17 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 18 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 19 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 20 | 1 | missing library member: System.Linq.Enumerable.OrderBy | linq/003_groupby_orderby |
| 21 | 1 | missing library member: System.Linq.Enumerable.Join | linq/004_join_tolist_todict |
| 22 | 1 | missing library member: System.Linq.Enumerable.Distinct | linq/005_aggregate_distinct_zip |
| 23 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 24 | 1 | missing library member: System.Linq.Enumerable.SelectMany | linq/006_selectmany_let |
| 25 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 26 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 27 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 28 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 29 | 1 | missing library member: System.Collections.Generic.Dictionary<,>.this[] | programs/003_bank_simulation |
| 30 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 31 | 1 | missing library member: System.Linq.Enumerable.Count | programs/005_word_frequency_linq |
| 32 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 33 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 34 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 35 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 36 | 1 | missing library member: string.Remove | types/022_string_basics |
