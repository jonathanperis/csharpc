Console.WriteLine($"{C.K} {C.R} {C.Computed}");
class C { public const int K = 3; public static readonly int R = K * 2; public static int Computed { get; } = R + K; }
