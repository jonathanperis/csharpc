// System.Xml / System.Xml.Linq compiled from the framework's IL: parsing (strings, files, streams in
// several encodings), LINQ to XML queries and construction, namespaces, XmlReader/XmlWriter, saving
// with an encoding that needs character entities, DTD entities, and XmlException positions.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

var doc = XDocument.Parse("""
    <?xml version="1.0" encoding="utf-8"?>
    <!-- inventory -->
    <catalog xmlns:x="urn:extra">
      <book id="b1" price="12.50"><title>C# in Depth</title><x:tag>lang</x:tag></book>
      <book id="b2" price="7"><title><![CDATA[<Fast> & Loose]]></title></book>
      <?render mode="fast"?>
      <magazine id="m1"><title>Monthly &amp; Weekly</title></magazine>
    </catalog>
    """);
Console.WriteLine($"root {doc.Root!.Name} declaration {doc.Declaration} nodes {doc.Nodes().Count()}");
foreach (var book in doc.Root.Elements("book"))
    Console.WriteLine($"{(string?)book.Attribute("id")}: {(string?)book.Element("title")} at {(decimal)book.Attribute("price")!}");
XNamespace x = "urn:extra";
Console.WriteLine($"tag {doc.Descendants(x + "tag").Single().Value}, total {doc.Root.Elements("book").Sum(b => (decimal)b.Attribute("price")!)}");
Console.WriteLine($"titles: {string.Join(" | ", doc.Descendants("title").Select(t => t.Value))}");
Console.WriteLine($"pi: {doc.Root.Nodes().OfType<XProcessingInstruction>().Single()}");

// Construction, ToString, attributes and namespaces.
XNamespace ns = "urn:orders";
var order = new XElement(ns + "order", new XAttribute("id", 42), new XAttribute(XNamespace.Xmlns + "o", ns),
    new XElement(ns + "line", new XAttribute("sku", "A-1"), new XAttribute("qty", 2), "Widget"),
    new XElement(ns + "line", new XAttribute("sku", "B<2>"), new XAttribute("qty", 1), "Gadget & \"Co\""),
    new XComment("end"));
Console.WriteLine(order);
Console.WriteLine(order.ToString(SaveOptions.DisableFormatting));
order.Element(ns + "line")!.SetAttributeValue("qty", 5);
order.Elements(ns + "line").Last().Remove();
Console.WriteLine($"after edits: {order.ToString(SaveOptions.DisableFormatting)}");

// Whitespace preservation.
var spaced = XElement.Parse("<a>\n  <b> x </b>\n</a>", LoadOptions.PreserveWhitespace);
Console.WriteLine($"preserved: {spaced.ToString(SaveOptions.DisableFormatting).Replace("\n", "\\n")}");

// XmlWriter to a StringBuilder, XmlReader over a string.
var sb = new StringBuilder();
using (var writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }))
{
    writer.WriteStartElement("root");
    writer.WriteAttributeString("version", "1.0");
    writer.WriteElementString("item", "one < two");
    writer.WriteStartElement("empty");
    writer.WriteEndElement();
    writer.WriteCData("raw ]]> text");
    writer.WriteEndElement();
}
Console.WriteLine(sb);
using (var reader = XmlReader.Create(new StringReader("<r a='1'><c>text</c><!--note--><d/></r>")))
{
    while (reader.Read())
        Console.WriteLine($"{reader.NodeType} {reader.Name} '{reader.Value}' depth {reader.Depth} attrs {reader.AttributeCount}");
}

// Saving through encodings: UTF-8 with a BOM, ASCII (character entities for what it can't encode).
var text = new XElement("t", "café € 😀", new XAttribute("note", "naïve"));
foreach (var encoding in new Encoding[] { new UTF8Encoding(true), Encoding.ASCII, Encoding.Unicode })
{
    var stream = new MemoryStream();
    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = encoding }))
        text.Save(writer);
    byte[] bytes = stream.ToArray();
    Console.WriteLine($"{encoding.WebName}: {bytes.Length} bytes, {Convert.ToHexString(bytes.Take(6).ToArray())}... -> {XElement.Load(new MemoryStream(bytes)).Value}");
}
Console.WriteLine(Encoding.ASCII.GetString(SaveAscii(text)));

// Files: XDocument.Save / Load, and XElement.Load(path).
string path = Path.Combine(Path.GetTempPath(), $"csharpc-546-{Environment.ProcessId}.xml");
doc.Save(path);
Console.WriteLine($"file starts: {File.ReadAllText(path)[..60].Replace("\r", "").Replace("\n", "\\n")}");
Console.WriteLine($"reloaded {XElement.Load(path).Elements().Count()} elements, {XDocument.Load(path).Descendants("title").Count()} titles");
File.Delete(path);

// DTD entities, and errors with their positions.
var withDtd = XDocument.Parse("<!DOCTYPE r [<!ENTITY who \"world\">]><r>hello &who;</r>");
Console.WriteLine($"entity: {withDtd.Root!.Value}");
foreach (var bad in new[] { "<a><b></a>", "<a x='1' x='2'/>", "<a>&undefined;</a>", "text", "<a></a><b/>" })
{
    try { XDocument.Parse(bad); }
    catch (XmlException e) { Console.WriteLine($"XmlException ({e.LineNumber},{e.LinePosition}): {e.Message}"); }
}
Console.WriteLine($"XmlConvert: {XmlConvert.ToString(3.5)} {XmlConvert.ToBoolean(" true ")} {XmlConvert.EncodeName("a b")} {XmlConvert.ToDateTime("2024-02-29T10:00:00Z", XmlDateTimeSerializationMode.Utc):O}");

static byte[] SaveAscii(XElement element)
{
    var stream = new MemoryStream();
    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = Encoding.ASCII, OmitXmlDeclaration = true }))
        element.Save(writer);
    return stream.ToArray();
}
