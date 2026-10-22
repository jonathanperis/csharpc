var gate = new object();
int counter = 0;
lock (gate) { counter++; }
var l = new System.Threading.Lock();
lock (l) { counter++; }
Console.WriteLine(counter);
