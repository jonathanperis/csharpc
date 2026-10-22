int[][] j = new int[3][];
for (int i = 0; i < 3; i++) { j[i] = new int[i + 1]; j[i][i] = i; }
Console.WriteLine(j[2][2] + " " + j[1].Length);
