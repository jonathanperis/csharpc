// RuntimeTypeHandle.CreateInstanceForAnotherGenericParameter with typeof constants (issue #224), called from the IL library
// tools/il-first/slicer/Synth.cs writes (`dotnet Slicer.dll synth-create-instance`): the member is internal to CoreLib, so no C#
// program can call it. tests/slicer.sh compiles this program against that library and compares with create_instance.expected.
using System;
using System.Collections.Generic;

Console.WriteLine(Synth.Make<int>());
Console.WriteLine(Synth.Make<string>());
Console.WriteLine(Synth.Make<DateTime>());
Console.WriteLine(Synth.Make<List<int>>());
Console.WriteLine(Synth.Make<int, long>());
Console.WriteLine(Synth.Make<string, Guid>());
Console.WriteLine(Synth.Make<int>().GetType().Name);
Console.WriteLine(Synth.Make<int>().GetType() == Synth.Make<int>().GetType());
Console.WriteLine(Synth.Make<int>().GetType() == Synth.Make<long>().GetType());
