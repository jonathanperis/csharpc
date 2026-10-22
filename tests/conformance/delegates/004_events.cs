var b = new Button();
b.Click += (s, e) => Console.WriteLine("clicked 1");
b.Click += (s, e) => Console.WriteLine("clicked 2");
b.Press();
class Button { public event EventHandler? Click; public void Press() => Click?.Invoke(this, EventArgs.Empty); }
