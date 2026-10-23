// float.Parse rounds the decimal digits straight to float. Going through the nearest double first
// would round twice: these inputs lie within 1e-26 of a midpoint between two floats, so their
// nearest double is the midpoint itself and ties-to-even would pick the wrong neighbour.
using System;
using System.Globalization;

string[] inputs =
[
    "1.00000005960464477539062501", "1.00000005960464477539062499", "1.000000059604644775390625",
    "1.00000017881393432617187501", "1.00000017881393432617187499", "1.000000178813934326171875",
    "3.40282356779733661637539395458142568448e38", "3.40282356779733661637539395458142568447e38",
    "1.4012984643248170709237295832899161312802619418765e-45",
    "7.006492321624085354618647916449580656401309709382578858785341419448955413429303e-46",
    "7.006492321624085354618647916449580656401309709382578858785341419448955413429304e-46",
    "16777217", "16777217.000000000000000001", "0.1", "3.14159", "-2.5e-3", "1e39", "-1e-50",
];
foreach (var s in inputs)
{
    float f = float.Parse(s, CultureInfo.InvariantCulture);
    Console.WriteLine($"{s} -> {f.ToString("R", CultureInfo.InvariantCulture)} 0x{BitConverter.SingleToInt32Bits(f):X8}");
    Console.WriteLine(float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var g) && g.Equals(f));
}
Console.WriteLine(float.Parse("1,00000005960464477539062501", new CultureInfo("de-DE")).ToString(CultureInfo.InvariantCulture));
