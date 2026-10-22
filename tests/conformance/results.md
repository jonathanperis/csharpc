# Conformance results (2026-10-22, csharpc -O2)

**57/195 programs pass (29.2%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 1 | 12 | 8.3% |
| classes | 14 | 15 | 93.3% |
| collections | 0 | 8 | 0.0% |
| delegates | 0 | 5 | 0.0% |
| exceptions | 1 | 10 | 10.0% |
| expressions | 3 | 20 | 15.0% |
| formatting | 1 | 5 | 20.0% |
| generics | 0 | 8 | 0.0% |
| interfaces | 0 | 9 | 0.0% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 0 | 6 | 0.0% |
| lexical | 9 | 10 | 90.0% |
| linq | 0 | 6 | 0.0% |
| localfunctions | 1 | 4 | 25.0% |
| members | 12 | 18 | 66.7% |
| patterns | 3 | 8 | 37.5% |
| programs | 1 | 6 | 16.7% |
| statements | 7 | 12 | 58.3% |
| types | 4 | 25 | 16.0% |
| **total** | **57** | **195** | **29.2%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 17 | user-defined struct '*' | bcl/004_span_memory, bcl/007_datetime_timespan, bcl/011_biginteger |
| 2 | 16 | interface implementation on '*' | collections/006_list_of_objects, expressions/012_target_typed_new, generics/007_generic_interface |
| 3 | 12 | creating instances of '*' | collections/001_list, collections/003_hashset, collections/004_queue_stack |
| 4 | 11 | try/catch (exception handling) | collections/002_dictionary, exceptions/001_try_catch, exceptions/002_finally |
| 5 | 9 | lambdas and delegates | delegates/001_func_action, delegates/002_custom_delegate, delegates/003_multicast |
| 6 | 7 | struct '*' | expressions/016_user_defined_conversions_chain, generics/004_generic_struct, programs/004_matrix_struct |
| 7 | 7 | interface '*' | generics/007_generic_interface, interfaces/001_basic, interfaces/002_multiple |
| 8 | 6 | generic method '*' | expressions/008_default_literal, generics/002_generic_methods, generics/003_constraints |
| 9 | 5 | '*' has no implementation in the csharpc runtime yet | bcl/006_environment, classes/011_is_as_cast, expressions/007_nameof_typeof_sizeof |
| 10 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 11 | 4 | nullable value type '*' | expressions/003_null_conditional, expressions/020_lifted_operators, types/008_nullable_value |
| 12 | 4 | foreach over '*' (only arrays so far) | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 13 | 4 | operation YieldReturn | iterators/001_yield_return, iterators/002_yield_break_lazy, iterators/003_iterator_finally |
| 14 | 4 | missing library member: string.Join | linq/005_aggregate_distinct_zip, linq/006_selectmany_let, programs/001_quicksort |
| 15 | 3 | closure: local function or lambda capturing '*' | expressions/018_short_circuit_side_effects, localfunctions/002_capturing, localfunctions/003_nested_in_method |
| 16 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 17 | 2 | collection expression of type '*' | expressions/010_collection_expressions, members/015_params_span |
| 18 | 2 | interpolation alignment/format specifiers | expressions/015_string_interpolation, formatting/005_interpolation_alignment |
| 19 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 20 | 2 | generic type '*' | generics/001_generic_class, generics/008_static_generic_cache |
| 21 | 2 | generic type parameter '*' | generics/001_generic_class, generics/005_nested_instantiations |
| 22 | 2 | missing library member: System.Func<int, int>.Invoke | lambdas/003_higher_order, lambdas/005_capture_this |
| 23 | 2 | internal compiler error: invalid LLVM IR | members/011_partial_methods, members/012_partial_properties |
| 24 | 2 | tuple values (tuples need struct support) | patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 25 | 2 | System.Decimal | programs/003_bank_simulation, types/003_decimal |
| 26 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 27 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 28 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 29 | 1 | missing library member: System.IO.TextWriter.WriteLine | bcl/005_console_apis |
| 30 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 31 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 32 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 33 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 34 | 1 | delegate '*' | delegates/002_custom_delegate |
| 35 | 1 | operation EventAssignment | delegates/004_events |
| 36 | 1 | operation EventReference | delegates/004_events |
| 37 | 1 | deriving from library class '*' | exceptions/003_custom |
| 38 | 1 | wrong exit code (-N, expected N) | exceptions/008_unhandled |
| 39 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 40 | 1 | array index of type '*' | expressions/009_ranges_indices |
| 41 | 1 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous |
| 42 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 43 | 1 | missing library member: System.Console.WriteLine | formatting/002_composite |
| 44 | 1 | missing library member: System.Collections.Generic.List<int>.Add | generics/007_generic_interface |
| 45 | 1 | missing library member: int.CompareTo | interfaces/007_icomparable |
| 46 | 1 | missing library member: System.Func<int>.Invoke | lambdas/001_closure_counter |
| 47 | 1 | missing library member: System.Linq.Enumerable.Range | linq/001_method_syntax |
| 48 | 1 | operation TranslatedQuery | linq/002_query_syntax |
| 49 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 50 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 51 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 52 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 53 | 1 | missing library member: System.Linq.Enumerable.ThenBy | programs/005_word_frequency_linq |
| 54 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 55 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 56 | 1 | operation None | statements/009_fixed |
| 57 | 1 | missing library member: double.IsNaN | types/002_floating |
| 58 | 1 | missing library member: char.CompareTo | types/005_bool_char |
| 59 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 60 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 61 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 62 | 1 | missing library member: object.ReferenceEquals | types/021_object_methods |
| 63 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
