// double.ConvertToIntegerNative<int/long> is the platform's own truncating conversion (#351): exact toward zero in range everywhere;
// for NaN, infinities and out-of-range values the instruction decides (x64's cvttsd2si: the integer minimum; Arm64's fcvtzs:
// saturation, NaN as 0). The out-of-range half is recorded live from .NET on each machine (tests/platform-skips.txt), since the
// answer depends on the architecture. Random's legacy generator converts this way on every Next call.
using System;
using System.Runtime.InteropServices;

static class Program
{
    static void Main()
    {
        double[] inRange = [0.0, 1.9, -1.9, 0.5, -0.5, 2147483647.0, -2147483648.0, 2147483647.9, -2147483648.9, 123456789.75];
        foreach (var d in inRange) Console.WriteLine($"{d:R}: int {double.ConvertToIntegerNative<int>(d)}");
        double[] inLongRange = [2147483648.0, -2147483649.0, 1e10, -1e10, 9.2e18, -9.2e18];
        foreach (var d in inLongRange) Console.WriteLine($"{d:R}: long {double.ConvertToIntegerNative<long>(d)}");

        // A scaled sample, as Random's CompatSeedImpl.Next(maxValue) computes it.
        var sum = 0L;
        for (var i = 0; i < 1000; i++) sum += double.ConvertToIntegerNative<int>(i * (1.0 / 1000) * 997);
        Console.WriteLine("scaled sum " + sum);

        var architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Console.WriteLine("out of range: no native conversion instruction defined for " + architecture);
            return;
        }
        double[] outOfRange = [2147483648.0, -2147483649.0, 1e10, -1e10, 9.3e18, -9.3e18, 1e300, double.NaN, double.PositiveInfinity, double.NegativeInfinity];
        foreach (var d in outOfRange)
            Console.WriteLine($"{d:R}: int {double.ConvertToIntegerNative<int>(d)} long {double.ConvertToIntegerNative<long>(d)}");
    }
}
