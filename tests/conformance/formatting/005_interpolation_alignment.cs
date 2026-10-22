string[] names = { "a", "bbb", "cc" };
int[] vals = { 1, 200, 30 };
for (int i = 0; i < names.Length; i++) Console.WriteLine($"|{names[i],-5}|{vals[i],5}|{vals[i],5:D4}|");
