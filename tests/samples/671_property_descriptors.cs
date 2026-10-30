// PropertyDescriptor, AttributeCollection and PropertyDescriptorCollection (corelib ports): derived
// descriptors, attribute defaults through the 'Default' field, value-changed events, lookup by name.
#nullable enable
using System;
using System.ComponentModel;

class NamePropertyDescriptor : PropertyDescriptor
{
    public NamePropertyDescriptor() : base("Name", new Attribute[] { new DescriptionAttribute("The name"), new CategoryAttribute("Data") }) { }
    public override Type ComponentType => typeof(Person);
    public override bool IsReadOnly => false;
    public override Type PropertyType => typeof(string);
    public override bool CanResetValue(object component) => false;
    public override object? GetValue(object? component) => ((Person)component!).Name;
    public override void ResetValue(object component) { }
    public override void SetValue(object? component, object? value) { ((Person)component!).Name = (string?)value; OnValueChanged(component, EventArgs.Empty); }
    public override bool ShouldSerializeValue(object component) => true;
}

class Person { public string? Name; }

class Program
{
    static void Main()
    {
        var pd = new NamePropertyDescriptor();
        var p = new Person { Name = "Ada" };
        Console.WriteLine(pd.Name + " " + pd.DisplayName + " " + pd.Description + " " + pd.Category + " " + pd.IsBrowsable + " " + pd.DesignTimeOnly + " " + pd.IsLocalizable);
        Console.WriteLine(pd.Attributes.Count);
        Console.WriteLine(pd.GetValue(p));
        pd.AddValueChanged(p, (s, e) => Console.WriteLine("changed " + ((Person)s!).Name));
        pd.SetValue(p, "Grace");
        Console.WriteLine(pd.Converter.GetType().Name);
        Console.WriteLine(pd.Equals(new NamePropertyDescriptor()) + " " + (pd.GetHashCode() == new NamePropertyDescriptor().GetHashCode()));
        var collection = new PropertyDescriptorCollection(new PropertyDescriptor[] { pd });
        Console.WriteLine(collection.Count + " " + (collection["Name"] == pd) + " " + (collection.Find("name", true) == pd) + " " + (collection["x"] == null));
        var attrs = new AttributeCollection(new BrowsableAttribute(false));
        Console.WriteLine(((BrowsableAttribute)attrs[typeof(BrowsableAttribute)]!).Browsable);
        Console.WriteLine(((ReadOnlyAttribute)attrs[typeof(ReadOnlyAttribute)]!).IsReadOnly);
        Console.WriteLine(attrs.Contains(new BrowsableAttribute(false)) + " " + attrs.Matches(BrowsableAttribute.No));
        Console.WriteLine(AttributeCollection.FromExisting(attrs, new BrowsableAttribute(true), new DescriptionAttribute("d")).Count);
        try { new NamePropertyDescriptorBad(); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
    }
}

class NamePropertyDescriptorBad : PropertyDescriptor
{
    public NamePropertyDescriptorBad() : base("", null) { }
    public override Type ComponentType => typeof(Person);
    public override bool IsReadOnly => false;
    public override Type PropertyType => typeof(string);
    public override bool CanResetValue(object component) => false;
    public override object? GetValue(object? component) => null;
    public override void ResetValue(object component) { }
    public override void SetValue(object? component, object? value) { }
    public override bool ShouldSerializeValue(object component) => true;
}
