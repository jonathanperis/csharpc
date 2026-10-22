# Conformance results (2026-10-22, csharpc -O2)

**92/195 programs pass (47.2%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 2 | 12 | 16.7% |
| classes | 14 | 15 | 93.3% |
| collections | 0 | 8 | 0.0% |
| delegates | 4 | 5 | 80.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 7 | 20 | 35.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 0 | 8 | 0.0% |
| interfaces | 0 | 9 | 0.0% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 5 | 6 | 83.3% |
| lexical | 10 | 10 | 100.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 3 | 4 | 75.0% |
| members | 16 | 18 | 88.9% |
| patterns | 3 | 8 | 37.5% |
| programs | 3 | 6 | 50.0% |
| statements | 9 | 12 | 75.0% |
| types | 11 | 25 | 44.0% |
| **total** | **92** | **195** | **47.2%** |

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
| 10 | 3 | missing library member: System.Reflection.MemberInfo.Name | classes/011_is_as_cast, expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 11 | 3 | missing library member: int.CompareTo | collections/001_list, collections/006_list_of_objects, interfaces/007_icomparable |
| 12 | 3 | generic type parameter '*' | generics/001_generic_class, generics/004_generic_struct, generics/005_nested_instantiations |
| 13 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 14 | 2 | record struct '*' | expressions/016_user_defined_conversions_chain, types/016_record_struct |
| 15 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 16 | 2 | generic type '*' | generics/001_generic_class, generics/008_static_generic_cache |
| 17 | 2 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let |
| 18 | 2 | pattern RecursivePattern | patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 19 | 2 | System.Decimal | programs/003_bank_simulation, types/003_decimal |
| 20 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 21 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 22 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 23 | 1 | operation ImplicitIndexerReference | bcl/004_span_memory |
| 24 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 25 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 26 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 27 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 28 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 29 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 30 | 1 | creating instances of 'System.Collections.Generic.HashSet<>' | collections/003_hashset |
| 31 | 1 | creating instances of 'System.Collections.Generic.Queue<>' | collections/004_queue_stack |
| 32 | 1 | creating instances of 'System.Collections.Generic.SortedDictionary<,>' | collections/005_sorted_linked |
| 33 | 1 | creating instances of 'System.Collections.Generic.PriorityQueue<,>' | collections/007_priority_queue |
| 34 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 35 | 1 | static field 'System.EventArgs.Empty' of a library type | delegates/004_events |
| 36 | 1 | deriving from library class 'System.Exception' | exceptions/003_custom |
| 37 | 1 | creating instances of 'System.ArgumentOutOfRangeException' | exceptions/009_exception_tostring |
| 38 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 39 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 40 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 41 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 42 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 43 | 1 | generic struct '*' | generics/004_generic_struct |
| 44 | 1 | missing library member: System.Collections.Generic.List<>.Add | generics/007_generic_interface |
| 45 | 1 | creating instances of 'System.Collections.Generic.List<System.Func<>>' | lambdas/002_loop_capture |
| 46 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 47 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 48 | 1 | foreach over 'System.Linq.IOrderedEnumerable<System.Linq.IGrouping<,,>>' (only arrays so far) | linq/003_groupby_orderby |
| 49 | 1 | missing library member: System.Linq.IGrouping<,,>.Key | linq/003_groupby_orderby |
| 50 | 1 | missing library member: System.Linq.Enumerable.Sum | linq/005_aggregate_distinct_zip |
| 51 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 52 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 53 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 54 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 55 | 1 | struct 'System.Collections.Generic.KeyValuePair<,>' | programs/003_bank_simulation |
| 56 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 57 | 1 | missing library member: System.Linq.IGrouping<,>.Key | programs/005_word_frequency_linq |
| 58 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 59 | 1 | missing library member: double.IsNaN | types/002_floating |
| 60 | 1 | missing library member: nint.Size | types/004_nint |
| 61 | 1 | missing library member: char.CompareTo | types/005_bool_char |
| 62 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 63 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 64 | 1 | missing library member: System.Array.Sort | types/010_arrays_1d |
| 65 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 66 | 1 | compiler crash: System.Collections.Generic.KeyNotFoundException | types/018_readonly_struct |
| 67 | 1 | missing library member: object.ReferenceEquals | types/021_object_methods |
| 68 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
