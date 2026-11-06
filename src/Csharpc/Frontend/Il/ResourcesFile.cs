using System.Text;

namespace Csharpc.Frontend.Il;

/// <summary>
/// The entries of a `.resources` file (System.Resources.ResourceReader's format, versions 1 and 2) without
/// ResourceReader's check of the reader type named in the header: a file the SDK writes with
/// GenerateResourceUsePreserializedResources names System.Resources.Extensions.DeserializingResourceReader there,
/// which ResourceReader refuses. Only what the compiler needs is decoded: the name of each entry, and its text
/// when it is a string; everything else is recorded by type code (or user type name).
/// </summary>
public static class ResourcesFile
{
    private const int Magic = unchecked((int)0xBEEFCACE);
    private const int StringCode = 1;
    private const int StartOfUserTypes = 0x40;

    /// <param name="Text">The string value, or null for other types.</param>
    /// <param name="TypeName">The entry's type: "System.String", a primitive's full name, "System.Byte[]", "System.IO.Stream" or a user type's full name.</param>
    /// <param name="Value">A primitive's (or byte[]'s, base64) value in invariant text, for the types csharpc serves from GetObject; else null.</param>
    /// <param name="Format">For a preserialized user type, how its data is stored (PreserializedResourceWriter's SerializationFormat:
    /// 1 BinaryFormatter, 2 TypeConverter byte array, 3 TypeConverter string, 4 activator stream); 0 otherwise.</param>
    /// <param name="AssemblyQualifiedName">A user type's name as the file spells it ("System.Drawing.Point, System.Drawing.Primitives").</param>
    public sealed record Entry(string Name, string? Text, string TypeName, string? Value = null, int Format = 0, string? AssemblyQualifiedName = null);

    public static IEnumerable<Entry> Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream, Encoding.UTF8);
        if (reader.ReadInt32() != Magic) throw new InvalidDataException("not a .resources file (bad magic number)");
        var headerVersion = reader.ReadInt32();
        var headerSize = reader.ReadInt32();
        if (headerSize < 0 || headerVersion < 1) throw new InvalidDataException("unsupported .resources header");
        stream.Seek(headerSize, SeekOrigin.Current); // the reader and resource set type names
        var version = reader.ReadInt32();
        if (version != 1 && version != 2) throw new InvalidDataException($"unsupported .resources version {version}");
        var count = reader.ReadInt32();
        var typeNames = new List<string>();
        if (version == 2)
        {
            var typeCount = reader.ReadInt32();
            for (var i = 0; i < typeCount; i++) typeNames.Add(reader.ReadString());
        }
        var alignment = (int)(stream.Position & 7);
        if (alignment != 0) stream.Seek(8 - alignment, SeekOrigin.Current);
        stream.Seek(4L * count, SeekOrigin.Current); // name hashes
        var namePositions = new int[count];
        for (var i = 0; i < count; i++) namePositions[i] = reader.ReadInt32();
        var dataSection = reader.ReadInt32();
        var nameSection = stream.Position;
        var entries = new List<Entry>(count);
        for (var i = 0; i < count; i++)
        {
            stream.Seek(nameSection + namePositions[i], SeekOrigin.Begin);
            var nameLength = Read7BitEncodedInt(reader);
            var name = Encoding.Unicode.GetString(reader.ReadBytes(nameLength));
            var dataOffset = reader.ReadInt32();
            stream.Seek(dataSection + dataOffset, SeekOrigin.Begin);
            var code = Read7BitEncodedInt(reader);
            if (code == StringCode) { entries.Add(new Entry(name, reader.ReadString(), "System.String")); continue; }
            var format = code >= StartOfUserTypes ? reader.ReadByte() : 0;
            var value = ValueOf(code, format, reader);
            var qualified = code >= StartOfUserTypes && code - StartOfUserTypes < typeNames.Count ? typeNames[code - StartOfUserTypes] : null;
            entries.Add(new Entry(name, null, TypeNameOf(code, typeNames), value, format, qualified));
        }
        return entries;
    }

    private static string TypeNameOf(int code, List<string> userTypes) => code switch
    {
        0 => "null", 2 => "System.Boolean", 3 => "System.Char", 4 => "System.Byte", 5 => "System.SByte", 6 => "System.Int16", 7 => "System.UInt16",
        8 => "System.Int32", 9 => "System.UInt32", 10 => "System.Int64", 11 => "System.UInt64", 12 => "System.Single", 13 => "System.Double",
        14 => "System.Decimal", 15 => "System.DateTime", 16 => "System.TimeSpan", 32 => "System.Byte[]", 33 => "System.IO.Stream",
        _ when code >= StartOfUserTypes && code - StartOfUserTypes < userTypes.Count => FullName(userTypes[code - StartOfUserTypes]),
        _ => "type code " + code,
    };

    // "Ns.Type, Assembly, Version=..." -> "Ns.Type" (a generic's argument list is kept whole)
    private static string FullName(string assemblyQualified)
    {
        var depth = 0;
        for (var i = 0; i < assemblyQualified.Length; i++)
        {
            if (assemblyQualified[i] == '[') depth++;
            else if (assemblyQualified[i] == ']') depth--;
            else if (assemblyQualified[i] == ',' && depth == 0) return assemblyQualified[..i];
        }
        return assemblyQualified;
    }

    private static string? ValueOf(int code, int format, BinaryReader reader)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        return code switch
        {
            2 => reader.ReadBoolean() ? "True" : "False",
            3 => reader.ReadUInt16().ToString(invariant),
            4 => reader.ReadByte().ToString(invariant),
            5 => reader.ReadSByte().ToString(invariant),
            6 => reader.ReadInt16().ToString(invariant),
            7 => reader.ReadUInt16().ToString(invariant),
            8 => reader.ReadInt32().ToString(invariant),
            9 => reader.ReadUInt32().ToString(invariant),
            10 => reader.ReadInt64().ToString(invariant),
            11 => reader.ReadUInt64().ToString(invariant),
            12 => reader.ReadSingle().ToString("R", invariant),
            13 => reader.ReadDouble().ToString("R", invariant),
            14 => reader.ReadDecimal().ToString(invariant),
            15 or 16 => reader.ReadInt64().ToString(invariant), // DateTime / TimeSpan: their ticks
            32 => Convert.ToBase64String(reader.ReadBytes(reader.ReadInt32())),
            // a preserialized user type: format 3 is "the type converter's string" (the text from the .resx)
            >= StartOfUserTypes => format == 3 ? reader.ReadString() : null,
            _ => null,
        };
    }

    private static int Read7BitEncodedInt(BinaryReader reader)
    {
        var result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var b = reader.ReadByte();
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new InvalidDataException("bad 7-bit encoded integer");
    }
}
