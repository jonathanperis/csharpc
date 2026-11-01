#:property UseSystemResourceKeys=true
// With UseSystemResourceKeys .NET gives its exception messages as resource keys; csharpc's corelib writes the
// full messages, so csharpc refuses the setting instead of printing them differently. `dotnet run` prints:
//   ArgumentNull_Generic Arg_ParamName_Name, x
//   Arg_IndexOutOfRangeException
//   Format_InvalidStringWithValue, x
//   ArgumentOutOfRange_IndexMustBeLess Arg_ParamName_Name, index
using System;
using System.Collections.Generic;

try { ArgumentNullException.ThrowIfNull((object?)null, "x"); } catch (Exception e) { Console.WriteLine(e.Message); }
try { int[] a = new int[1]; a[Index()] = 1; } catch (Exception e) { Console.WriteLine(e.Message); }
try { int.Parse("x"); } catch (Exception e) { Console.WriteLine(e.Message); }
try { new List<int>()[3] = 1; } catch (Exception e) { Console.WriteLine(e.Message); }

static int Index() => 2;
