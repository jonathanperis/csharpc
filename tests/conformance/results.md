# Conformance results (2026-10-22, csharpc -O2)

**188/195 programs pass (96.4%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 4 | 4 | 100.0% |
| bcl | 11 | 12 | 91.7% |
| classes | 15 | 15 | 100.0% |
| collections | 8 | 8 | 100.0% |
| delegates | 5 | 5 | 100.0% |
| exceptions | 10 | 10 | 100.0% |
| expressions | 19 | 20 | 95.0% |
| formatting | 5 | 5 | 100.0% |
| generics | 7 | 8 | 87.5% |
| interfaces | 9 | 9 | 100.0% |
| iterators | 4 | 4 | 100.0% |
| lambdas | 6 | 6 | 100.0% |
| lexical | 10 | 10 | 100.0% |
| linq | 6 | 6 | 100.0% |
| localfunctions | 4 | 4 | 100.0% |
| members | 18 | 18 | 100.0% |
| patterns | 7 | 8 | 87.5% |
| programs | 6 | 6 | 100.0% |
| statements | 12 | 12 | 100.0% |
| types | 22 | 25 | 88.0% |
| **total** | **188** | **195** | **96.4%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 1 | missing library member: System.Tuple.Create | bcl/012_tuple_valuetuple_methods |
| 2 | 1 | collection expression of type 'System.Collections.Generic.List<>' | expressions/010_collection_expressions |
| 3 | 1 | runtime abort: Unhandled exception. System.InvalidCastException: Unable to cast object of type 'System.Collections. | generics/006_variance |
| 4 | 1 | operator GreaterThan on reference types | patterns/007_is_not_null_var |
| 5 | 1 | missing library member: System.Enum.HasFlag | types/006_enum_flags |
| 6 | 1 | missing library member: System.Enum.GetName | types/007_enum_basic |
| 7 | 1 | missing library member: string.Remove | types/022_string_basics |
