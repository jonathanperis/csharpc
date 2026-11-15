// FileStream (served from the framework's IL) refuses to open a file for writing while deserialization is in progress (System.Data's
// DataTable.ReadXml declares it through SerializationInfo.StartDeserialization), unless the AllowFileWrites switch says otherwise.
#pragma warning disable IL2026, IL3050 // DataTable XML serialization: trimming/AOT analyzer warnings only
using System;
using System.Data;
using System.IO;

var path = Path.Combine(Path.GetTempPath(), "csharpc-guard-" + Environment.ProcessId + ".txt");
var source = new DataTable("t");
source.Columns.Add("n", typeof(int));
source.Rows.Add(1);
var xml = new StringWriter();
source.WriteXml(xml, XmlWriteMode.WriteSchema);

var table = new DataTable();
table.RowChanged += (s, e) =>
{
    try
    {
        using (var f = new FileStream(path, FileMode.Create)) f.WriteByte(1);
        Console.WriteLine("write allowed");
    }
    catch (Exception ex) { Console.WriteLine(ex.GetType().FullName + ": " + ex.Message.Replace(path, "<path>")); }
    try
    {
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read)) Console.WriteLine("read allowed " + f.Length);
    }
    catch (Exception ex) { Console.WriteLine(ex.GetType().FullName + ": " + ex.Message.Replace(path, "<path>")); }
};
table.ReadXml(new StringReader(xml.ToString()));
Console.WriteLine(table.Rows.Count);
using (var f = new FileStream(path, FileMode.Create)) f.WriteByte(2);
Console.WriteLine("after: write allowed");
File.Delete(path);
