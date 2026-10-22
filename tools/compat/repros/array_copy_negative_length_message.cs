// Exception messages must match .NET: Array.Copy with a negative length throws
// ArgumentOutOfRangeException whose message is "length ('-1') must be a non-negative value.
// (Parameter 'length')\nActual value was -1." (ArgumentOutOfRangeException.ThrowNegative);
// csharpc prints the older "Non-negative number required. (Parameter 'length')", and its
// ParamName is empty instead of "length".
// Found in dotnet/docs docs/standard/exceptions/snippets/how-to-use-finally-blocks.
using System;

int[] src = { 1, 2, 3 }, dst = new int[3];
try
{
    Array.Copy(src, dst, -1);
}
catch (ArgumentOutOfRangeException e)
{
    Console.WriteLine(e.Message);
    Console.WriteLine(e.ParamName);
}
