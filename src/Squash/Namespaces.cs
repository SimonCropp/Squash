namespace Squash;

/// <summary>
/// The namespaces an assembly declares types in, and the root descriptor that keeps some of them
/// whole for friend assemblies. Read from the metadata by hand, for the reason PeFile gives.
/// </summary>
public static class Namespaces
{
    const int metadataSignature = 0x424A5342;

    // Table numbers, from ECMA-335 II.22.
    const int moduleTable = 0x00;
    const int typeRefTable = 0x01;
    const int typeDefTable = 0x02;
    const int fieldTable = 0x04;
    const int methodDefTable = 0x06;
    const int moduleRefTable = 0x1A;
    const int typeSpecTable = 0x1B;
    const int assemblyRefTable = 0x23;

    /// <summary>
    /// "Mine; Mine.Other*" gives "Mine" and "Mine.Other". A trailing star says what a prefix already
    /// means.
    /// </summary>
    public static List<string> Prefixes(string setting) =>
        setting
            .Split([';', ',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(_ => _.TrimEnd('*'))
            .Where(_ => _.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static bool Matches(string name, string prefix) =>
        name.StartsWith(prefix, StringComparison.Ordinal);

    public static List<string> Matching(IEnumerable<string> namespaces, IReadOnlyList<string> prefixes) =>
        namespaces
            .Where(name => prefixes.Any(prefix => Matches(name, prefix)))
            .ToList();

    /// <summary>
    /// Keeps every type of the namespaces, with all of its members. Never for an empty list: the
    /// linker reads an assembly element with nothing in it as the whole assembly.
    /// </summary>
    public static string Descriptor(string assemblyName, IReadOnlyList<string> namespaces)
    {
        if (namespaces.Count == 0)
        {
            throw new ArgumentException("A descriptor for no namespaces would keep the whole assembly.", nameof(namespaces));
        }

        // "\n" on every platform, so the file is the same wherever the build ran.
        var builder = new StringBuilder();
        builder.Append("<linker>\n");
        builder.Append($"  <assembly fullname=\"{Escape(assemblyName)}\">\n");
        foreach (var name in namespaces)
        {
            builder.Append($"    <namespace fullname=\"{Escape(name)}\" />\n");
        }

        builder.Append("  </assembly>\n");
        builder.Append("</linker>\n");
        return builder.ToString();
    }

    static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");

    /// <summary>
    /// Every namespace with a type in it, in ordinal order. The global namespace has no name and is
    /// not one of them.
    /// </summary>
    public static List<string> Read(byte[] image)
    {
        try
        {
            return Parse(image);
        }
        catch (Exception exception) when
            (exception is
                 IndexOutOfRangeException or
                 ArgumentException or
                 OverflowException)
        {
            // An offset in the metadata pointed outside the file.
            throw new BadImageFormatException("The metadata could not be read.", exception);
        }
    }

    static List<string> Parse(byte[] image)
    {
        var root = PeFile.Read(image).MetadataOffset;
        if (PeFile.Int32(image, root) != metadataSignature)
        {
            throw new BadImageFormatException("No metadata.");
        }

        // Past the version string, whose padded length is given, to the flags and the stream count.
        var position = root + 16 + PeFile.Int32(image, root + 12);
        var streams = PeFile.UInt16(image, position + 2);
        position += 4;

        var tables = 0;
        var strings = 0;
        for (var index = 0; index < streams; index++)
        {
            var offset = root + PeFile.Int32(image, position);
            var nameEnd = Terminator(image, position + 8);
            var name = Encoding.ASCII.GetString(image, position + 8, nameEnd - (position + 8));
            if (name is "#~" or "#-")
            {
                tables = offset;
            }

            if (name == "#Strings")
            {
                strings = offset;
            }

            // The name with its terminator is padded to four bytes.
            position += 8 + (name.Length + 4) / 4 * 4;
        }

        if (tables == 0 ||
            strings == 0)
        {
            throw new BadImageFormatException("No metadata tables.");
        }

        var heapSizes = image[tables + 6];
        var valid = (uint)PeFile.Int32(image, tables + 8) |
                    (ulong)(uint)PeFile.Int32(image, tables + 12) << 32;

        // A row count for each table that is present, in order of table number.
        var rows = new int[64];
        position = tables + 24;
        for (var table = 0; table < rows.Length; table++)
        {
            if ((valid >> table & 1) == 0)
            {
                continue;
            }

            rows[table] = PeFile.Int32(image, position);
            position += 4;
        }

        // An uncompressed table stream may have four more bytes here.
        if ((heapSizes & 0x40) != 0)
        {
            position += 4;
        }

        var stringIndex = Width((heapSizes & 0x01) != 0);
        var guidIndex = Width((heapSizes & 0x02) != 0);

        // A coded index is a row of one of several tables, with two bits here to say which.
        var resolutionScope = Coded(rows, moduleTable, moduleRefTable, assemblyRefTable, typeRefTable);
        var typeDefOrRef = Coded(rows, typeDefTable, typeRefTable, typeSpecTable);

        var moduleRow = 2 + stringIndex + 3 * guidIndex;
        var typeRefRow = resolutionScope + 2 * stringIndex;
        var typeDefRow = 4 +
                         2 * stringIndex +
                         typeDefOrRef +
                         Width(rows[fieldTable] > 0xFFFF) +
                         Width(rows[methodDefTable] > 0xFFFF);

        // The tables follow in order of table number, and these are the first three.
        var typeDefs = position +
                       rows[moduleTable] * moduleRow +
                       rows[typeRefTable] * typeRefRow;

        var found = new SortedSet<string>(StringComparer.Ordinal);
        for (var row = 0; row < rows[typeDefTable]; row++)
        {
            // Flags, the name, then the namespace.
            var entry = typeDefs + row * typeDefRow + 4 + stringIndex;
            var start = strings + Index(image, entry, stringIndex);
            var name = Encoding.UTF8.GetString(image, start, Terminator(image, start) - start);

            // Empty for the global namespace, and for a nested type, which is in the namespace of
            // the type it is nested in.
            if (name.Length > 0)
            {
                found.Add(name);
            }
        }

        return found.ToList();
    }

    static int Width(bool wide)
    {
        if (wide)
        {
            return 4;
        }

        return 2;
    }

    static int Coded(int[] rows, params int[] tables) =>
        Width(tables.Max(_ => rows[_]) >= 1 << 14);

    static int Index(byte[] image, int offset, int width)
    {
        if (width == 4)
        {
            return PeFile.Int32(image, offset);
        }

        return PeFile.UInt16(image, offset);
    }

    static int Terminator(byte[] image, int start)
    {
        var end = start;
        while (image[end] != 0)
        {
            end++;
        }

        return end;
    }
}
