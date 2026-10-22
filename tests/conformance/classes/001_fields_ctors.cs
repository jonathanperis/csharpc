var a = new Account("ann", 10);
a.Deposit(5);
Console.WriteLine(a);
class Account
{
    private readonly string _owner;
    private int _balance;
    public Account(string owner, int balance) { _owner = owner; _balance = balance; }
    public void Deposit(int x) => _balance += x;
    public override string ToString() => _owner + ":" + _balance;
}
