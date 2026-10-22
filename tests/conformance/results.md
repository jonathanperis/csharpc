# Conformance results (2026-10-22, csharpc -O2)

**116/195 programs pass (59.5%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 0 | 4 | 0.0% |
| bcl | 2 | 12 | 16.7% |
| classes | 14 | 15 | 93.3% |
| collections | 5 | 8 | 62.5% |
| delegates | 3 | 5 | 60.0% |
| exceptions | 2 | 10 | 20.0% |
| expressions | 9 | 20 | 45.0% |
| formatting | 3 | 5 | 60.0% |
| generics | 6 | 8 | 75.0% |
| interfaces | 6 | 9 | 66.7% |
| iterators | 0 | 4 | 0.0% |
| lambdas | 6 | 6 | 100.0% |
| lexical | 9 | 10 | 90.0% |
| linq | 5 | 6 | 83.3% |
| localfunctions | 4 | 4 | 100.0% |
| members | 15 | 18 | 83.3% |
| patterns | 3 | 8 | 37.5% |
| programs | 4 | 6 | 66.7% |
| statements | 8 | 12 | 66.7% |
| types | 12 | 25 | 48.0% |
| **total** | **116** | **195** | **59.5%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 11 | try/catch (exception handling) | collections/002_dictionary, exceptions/001_try_catch, exceptions/002_finally |
| 2 | 5 | operation YieldReturn | interfaces/009_ienumerable_impl, iterators/001_yield_return, iterators/002_yield_break_lazy |
| 3 | 4 | async method '*' | async/001_async_main, async/002_task_whenall, async/003_valuetask_exceptions |
| 4 | 4 | record '*' | collections/006_list_of_objects, expressions/012_target_typed_new, patterns/004_positional_patterns |
| 5 | 3 | missing library member: System.Reflection.MemberInfo.Name | classes/011_is_as_cast, expressions/007_nameof_typeof_sizeof, types/023_dynamic_free_var |
| 6 | 3 | System.Decimal | patterns/003_property_patterns, programs/003_bank_simulation, types/003_decimal |
| 7 | 3 | pattern RecursivePattern | patterns/004_positional_patterns, patterns/006_tuple_patterns, programs/006_state_machine_enum |
| 8 | 2 | struct 'System.Span<>' | bcl/004_span_memory, types/019_ref_struct_span |
| 9 | 2 | ref assignment | expressions/005_conditional, members/017_ref_returns_locals |
| 10 | 2 | operation AnonymousObjectCreation | expressions/014_with_expression_anonymous, linq/006_selectmany_let |
| 11 | 2 | record struct '*' | expressions/016_user_defined_conversions_chain, types/016_record_struct |
| 12 | 2 | missing library member: double.ToString | formatting/001_numeric_format_strings, formatting/003_custom_formats |
| 13 | 2 | missing library member: System.Array.Sort | interfaces/007_icomparable, types/010_arrays_1d |
| 14 | 2 | struct 'System.ReadOnlySpan<>' | lexical/007_utf8_literals, members/015_params_span |
| 15 | 1 | missing library member: System.Text.StringBuilder.Insert | bcl/001_stringbuilder |
| 16 | 1 | missing library member: string.Split | bcl/002_string_methods |
| 17 | 1 | missing library member: System.Math.Round | bcl/003_math |
| 18 | 1 | missing library member: System.Environment.ProcessorCount | bcl/006_environment |
| 19 | 1 | struct 'System.DateTime' | bcl/007_datetime_timespan |
| 20 | 1 | missing library member: double.Parse | bcl/009_char_parse_convert |
| 21 | 1 | missing library member: System.Numerics.BitOperations.PopCount | bcl/010_bitoperations |
| 22 | 1 | struct 'System.Numerics.BigInteger' | bcl/011_biginteger |
| 23 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 24 | 1 | missing library member: System.Array.Reverse | collections/008_array_methods |
| 25 | 1 | operator Add on reference types | delegates/003_multicast |
| 26 | 1 | operation EventAssignment | delegates/004_events |
| 27 | 1 | operation EventReference | delegates/004_events |
| 28 | 1 | deriving from library class 'System.Exception' | exceptions/003_custom |
| 29 | 1 | creating instances of 'System.ArgumentOutOfRangeException' | exceptions/009_exception_tostring |
| 30 | 1 | missing library member: System.Convert.ToInt32 | expressions/006_conversions_numeric |
| 31 | 1 | array index of type 'System.Index' | expressions/009_ranges_indices |
| 32 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 33 | 1 | missing library member: object.Equals | expressions/019_equality_reference_value |
| 34 | 1 | three-valued logic on bool? | expressions/020_lifted_operators |
| 35 | 1 | missing library member: System.Array.Resize | generics/001_generic_class |
| 36 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type 'System.Collections. | generics/006_variance |
| 37 | 1 | internal compiler error: invalid LLVM IR | interfaces/006_static_abstract |
| 38 | 1 | Extension '*' | members/010_extension_members_cs14 |
| 39 | 1 | closure: local function or lambda capturing parameter '*' | members/010_extension_members_cs14 |
| 40 | 1 | collection expression of type 'System.ReadOnlySpan<>' | members/015_params_span |
| 41 | 1 | pattern ListPattern | patterns/005_list_patterns |
| 42 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 43 | 1 | missing library member: System.Threading.Monitor.Enter | statements/007_lock |
| 44 | 1 | operation AddressOf | statements/008_unsafe_pointers |
| 45 | 1 | operation None | statements/009_fixed |
| 46 | 1 | missing library member: nint.Size | types/004_nint |
| 47 | 1 | formatting [Flags] enum '*' | types/006_enum_flags |
| 48 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 49 | 1 | multi-dimensional arrays | types/011_arrays_multidim |
| 50 | 1 | compiler crash: System.Collections.Generic.KeyNotFoundException | types/018_readonly_struct |
| 51 | 1 | missing library member: object.ReferenceEquals | types/021_object_methods |
| 52 | 1 | missing library member: string.IsNullOrWhiteSpace | types/022_string_basics |
