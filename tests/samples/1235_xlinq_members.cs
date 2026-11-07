// System.Xml.Linq members the package survey lists (XName operators, XContainer.Element(XName), XElement.Attribute(XName),
// XObjectChangeEventArgs.ObjectChange): used from the program and through XElement's change events.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

XNamespace ns = "urn:example";
XName local = "item";
XName qualified = ns + "item";
Console.WriteLine(local + " " + qualified + " " + qualified.LocalName + " " + qualified.NamespaceName);
Console.WriteLine((local == "item") + " " + (local != "item") + " " + (local != qualified) + " " + (local == XName.Get("item")) + " " + (qualified == XName.Get("item", "urn:example")));
Console.WriteLine(local.Equals(XName.Get("item")) + " " + ReferenceEquals(local, XName.Get("item")) + " " + (local == (XName)null) + " " + (local != (XName)null));

var root = new XElement(ns + "root",
    new XAttribute("id", "1"), new XAttribute(ns + "lang", "pt"),
    new XElement(ns + "item", new XAttribute("n", 1), "first"),
    new XElement(ns + "item", new XAttribute("n", 2), "second"),
    new XElement("plain", "no namespace"));
Console.WriteLine(root.Element(ns + "item").Value);
Console.WriteLine(root.Element("plain").Value);
Console.WriteLine(root.Element("missing") == null);
Console.WriteLine(root.Attribute("id").Value + " " + root.Attribute(ns + "lang").Value + " " + (root.Attribute("lang") == null));
Console.WriteLine(root.Elements(ns + "item").Select(e => (int)e.Attribute("n")).Sum());
Console.WriteLine(root.Name == ns + "root");

var log = new List<string>();
root.Changing += (sender, e) => log.Add("changing " + e.ObjectChange + " " + ((XObject)sender).GetType().Name);
root.Changed += (sender, e) => log.Add("changed " + e.ObjectChange);
root.Add(new XElement("added"));
root.Element("plain").Value = "changed text";
root.SetAttributeValue("id", "2");
root.SetAttributeValue("fresh", "yes");
root.Attribute("fresh").Remove();
root.Element("added").Remove();
root.Element(ns + "item").Name = ns + "renamed";
foreach (var line in log) Console.WriteLine(line);
Console.WriteLine(root);

var parsed = XElement.Parse("<a xmlns='urn:p'><b x='1'/><b x='2'><c/></b></a>");
XNamespace p = "urn:p";
Console.WriteLine(string.Join(",", parsed.Elements(p + "b").Select(b => b.Attribute("x").Value)));
Console.WriteLine(parsed.Element(p + "b").Element(p + "c") == null);
Console.WriteLine(parsed.Descendants(p + "c").Count());
