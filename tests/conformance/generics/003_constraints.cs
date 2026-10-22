Console.WriteLine(Create<Item>().Name);
Console.WriteLine(DefaultOf<int>() + " " + (DefaultOf<string>() == null));
static T Create<T>() where T : new() => new T();
static T? DefaultOf<T>() => default;
class Item { public string Name = "item"; }
