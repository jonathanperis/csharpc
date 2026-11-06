using Shop.App.Components;
using Shop.Library.Components;

// Each project's Razor generator runs with that project's RootNamespace: the library's component is
// Shop.Library.Components.Banner, the app's Shop.App.Components.Page.
Console.WriteLine(typeof(Banner).FullName);
Console.WriteLine(typeof(Page).FullName);
Console.WriteLine(typeof(Banner).Assembly.GetName().Name);
Console.WriteLine(typeof(Page).Assembly.GetName().Name);

Console.WriteLine(new Banner { Title = "Sale <today>", Subtitle = "50% off" }.RenderHtml());
Console.WriteLine(new Banner { Title = "Plain" }.RenderHtml());
Console.WriteLine(new Page { Name = "Ada & Bob" }.RenderHtml());
