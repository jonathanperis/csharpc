int calls = 0;
bool T() { calls++; return true; }
bool F() { calls++; return false; }
bool r = F() && T() || T() & F() | T();
Console.WriteLine(r + " " + calls);
