Repo<User> r = new UserRepo();
r.Add(new User("ann")); r.Add(new User("bob"));
Console.WriteLine(r.Describe());
class Repo<T> where T : class { protected List<T> items = new(); public void Add(T t) => items.Add(t); public virtual string Describe() => $"{typeof(T).Name}x{items.Count}"; }
class UserRepo : Repo<User> { public override string Describe() => base.Describe() + ": " + string.Join(",", items.Select(u => u.Name)); }
record User(string Name);
