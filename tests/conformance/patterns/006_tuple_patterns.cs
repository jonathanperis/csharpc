Console.WriteLine(Rps("rock", "scissors"));
Console.WriteLine(Rps("paper", "scissors"));
Console.WriteLine(Rps("rock", "rock"));
static string Rps(string a, string b) => (a, b) switch
{
    ("rock", "scissors") or ("paper", "rock") or ("scissors", "paper") => "first wins",
    var (x, y) when x == y => "tie",
    _ => "second wins",
};
