var e = new Employee("ann", 100);
Console.WriteLine(e.Describe());
class Person(string name) { protected string Name { get; } = name; public virtual string Describe() => $"person {Name}"; }
class Employee(string name, int salary) : Person(name) { public override string Describe() => base.Describe() + $" earns {salary}"; }
