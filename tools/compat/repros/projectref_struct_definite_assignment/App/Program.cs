Lib.Struct s;
Assign(out s);
System.Console.WriteLine("ok");

static void Assign(out Lib.Struct s)
{
    // No assignment: legal for an imported struct whose only fields are private.
}
