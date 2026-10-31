using Shop.Views.Components;
using Shop.Views.Model;

// The component classes come from the Razor source generator; their namespace from RootNamespace.
Console.WriteLine(typeof(Basket).FullName);
Console.WriteLine(typeof(Basket).BaseType?.FullName);

Console.WriteLine(new Basket
{
    Owner = "Ada <admin>",
    Lines = [new("A-1", "Apple", 3, 0.5m), new("F-7", "Fig", 1, 2.25m)],
}.RenderHtml());
Console.WriteLine(new Basket { Owner = "Bob" }.RenderHtml());

namespace Shop.Views.Model
{
    public sealed record Line(string Sku, string Name, int Quantity, decimal Price)
    {
        public decimal Total => Quantity * Price;
    }
}
