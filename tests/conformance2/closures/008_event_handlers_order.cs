var b = new Button();
b.Clicked += (s, e) => Console.WriteLine("first");
EventHandler h = (s, e) => Console.WriteLine("second");
b.Clicked += h;
b.Clicked += (s, e) => Console.WriteLine("third");
b.Click();
b.Clicked -= h;
b.Click();
class Button { public event EventHandler? Clicked; public void Click() => Clicked?.Invoke(this, EventArgs.Empty); }
