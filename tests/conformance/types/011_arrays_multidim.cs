int[,] m = new int[3, 4];
for (int i = 0; i < 3; i++)
    for (int j = 0; j < 4; j++)
        m[i, j] = i * 10 + j;
Console.WriteLine(m[2, 3]);
Console.WriteLine(m.Length + " " + m.GetLength(0) + " " + m.GetLength(1) + " " + m.Rank);
int[,] lit = { { 1, 2 }, { 3, 4 } };
Console.WriteLine(lit[1, 0]);
