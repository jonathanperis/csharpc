// System.Type (reflection-lite): GetType(), typeof, Name/FullName/Namespace, identity.
object[] things = [42, "text", 3.14, 'c', true, 2.5m, new Dog(), new Point(1, 2), Color.Green, new int[3], new string[0], TimeSpan.Zero];
foreach (var thing in things)
{
    var t = thing.GetType();
    Console.WriteLine($"{t.Name,-10} {t.FullName,-20} {t}");
}
Console.WriteLine(typeof(int) == 42.GetType());
Console.WriteLine(typeof(string).Name + " " + typeof(Animal).FullName + " " + typeof(Outer.Inner).FullName + " " + typeof(Outer.Inner).Name);
Animal a = new Dog();
Console.WriteLine(a.GetType() == typeof(Dog));
Console.WriteLine(a.GetType() == typeof(Animal));
Console.WriteLine(a.GetType() != typeof(Animal));
Console.WriteLine(typeof(Shapes.Circle).Namespace + " " + typeof(Shapes.Circle).FullName);
Console.WriteLine(typeof(double[]).Name + " " + typeof(Point[][]).FullName);
Console.WriteLine(new Shapes.Circle().GetType().Name);
Console.WriteLine(typeof(DayOfWeek).FullName + " " + DateTime.MinValue.DayOfWeek);
int x = 5;
Console.WriteLine(x.GetType().Name + " " + Color.Red.GetType().Name);
Console.WriteLine(new int[2].ToString() + " " + new Dog().ToString());
Console.WriteLine(nameof(Dog) + " " + typeof(Dog).Name);

class Animal { }
class Dog : Animal { }
struct Point(int x, int y) { public int X = x, Y = y; }
enum Color { Red, Green }
class Outer { public class Inner { } }
namespace Shapes { class Circle { } }
