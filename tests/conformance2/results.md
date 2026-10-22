# Conformance results (2026-10-22, csharpc -O2)

**252/258 programs pass (97.7%)**

| area | pass | total | coverage |
|---|---:|---:|---:|
| async | 6 | 6 | 100.0% |
| boxing | 5 | 5 | 100.0% |
| closures | 8 | 9 | 88.9% |
| collections | 8 | 10 | 80.0% |
| conversions | 12 | 12 | 100.0% |
| dispatch | 8 | 8 | 100.0% |
| evalorder | 12 | 12 | 100.0% |
| exceptions | 11 | 12 | 91.7% |
| formatting | 6 | 6 | 100.0% |
| generics | 8 | 8 | 100.0% |
| generics2 | 6 | 6 | 100.0% |
| inheritance | 5 | 5 | 100.0% |
| initializers | 4 | 4 | 100.0% |
| interfaces | 8 | 8 | 100.0% |
| interpolation | 7 | 7 | 100.0% |
| iterators | 7 | 7 | 100.0% |
| lambdas2 | 5 | 5 | 100.0% |
| linq | 10 | 11 | 90.9% |
| members | 8 | 8 | 100.0% |
| misc | 13 | 13 | 100.0% |
| misc2 | 14 | 15 | 93.3% |
| nullable | 6 | 6 | 100.0% |
| numerics | 8 | 8 | 100.0% |
| operators | 7 | 7 | 100.0% |
| overloads | 10 | 10 | 100.0% |
| patterns | 7 | 7 | 100.0% |
| records | 6 | 6 | 100.0% |
| spans | 5 | 5 | 100.0% |
| strings | 10 | 10 | 100.0% |
| structs | 9 | 9 | 100.0% |
| switches | 8 | 8 | 100.0% |
| tuples | 5 | 5 | 100.0% |
| **total** | **252** | **258** | **97.7%** |

## Failure reasons (ranked by programs blocked)

| # | programs | reason | e.g. |
|---:|---:|---|---|
| 1 | 3 | construct '*' (operation None) | collections/008_dictionary_api_edges, collections/010_array_methods_edges, linq/006_zip_chunk_distinct_comparer |
| 2 | 1 | missing library member: System.Convert.ToString | closures/009_method_group_conversions |
| 3 | 1 | wrong output | exceptions/003_filter_side_effect_order |
| 4 | 1 | runtime abort: Unhandled exception. System.OutOfMemoryException: Insufficient memory to continue the execution of t | misc2/011_interface_default_with_state_via_property |
