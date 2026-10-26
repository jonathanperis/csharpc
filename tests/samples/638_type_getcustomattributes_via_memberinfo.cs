// Attribute.GetCustomAttributes / CustomAttributeExtensions on a Type after GetTypeInfo().
using System.Reflection;
#pragma warning disable CS0612
TypeInfo typeInfo = typeof(MyClass).GetTypeInfo();
Console.WriteLine(typeInfo.Name);
foreach (var attr in typeInfo.GetCustomAttributes())
    Console.WriteLine(attr.GetType().Name);

[Obsolete]
class MyClass { }
#pragma warning restore CS0612
