namespace Squash;

/// <summary>
/// The documentation file the compiler writes beside an assembly, less the entries for what is no
/// longer in that assembly. Worked on as bytes, and only ever by leaving bytes out: what stays is
/// never parsed and written again, so every remaining entry is exactly as the compiler left it.
/// </summary>
public static class Documentation
{
    /// <summary>
    /// Takes out each member element whose name is one of those given. A name not given stays,
    /// whatever it names.
    /// </summary>
    public static TrimmedDocumentation Trim(byte[] xml, HashSet<string> removed)
    {
        var members = Members(xml)
            .Where(_ => removed.Contains(Unescape(_.Name)))
            .ToList();
        if (members.Count == 0)
        {
            return new(xml, 0);
        }

        using var stream = new MemoryStream(xml.Length);
        var position = 0;
        foreach (var member in members)
        {
            var (start, end) = WholeLines(xml, member.Start, member.End);
            stream.Write(xml, position, start - position);
            position = end;
        }

        stream.Write(xml, position, xml.Length - position);
        return new(stream.ToArray(), members.Count);
    }

    sealed record Member(int Start, int End, string Name);

    /// <summary>
    /// Every member element directly inside doc/members, with where it starts and ends. Enough of
    /// XML is read to know where an element ends, and no more: a member's content can hold markup,
    /// comments and character data of its own.
    /// </summary>
    static List<Member> Members(byte[] xml)
    {
        var members = new List<Member>();

        // The elements the position is inside.
        var open = new List<string>();
        var memberStart = 0;
        var memberName = "";
        var position = Start(xml);
        while (true)
        {
            position = Array.IndexOf(xml, (byte)'<', position);
            if (position < 0)
            {
                break;
            }

            if (At(xml, position, "<!--"))
            {
                position = After(xml, position + 4, "-->");
                continue;
            }

            if (At(xml, position, "<![CDATA["))
            {
                position = After(xml, position + 9, "]]>");
                continue;
            }

            if (At(xml, position, "<?"))
            {
                position = After(xml, position + 2, "?>");
                continue;
            }

            if (At(xml, position, "<!"))
            {
                throw new InvalidDataException("it has a document type declaration");
            }

            if (At(xml, position, "</"))
            {
                var close = After(xml, position + 2, ">");
                var closed = Text(xml, position + 2, close - 1).Trim();
                if (open.Count == 0 ||
                    open[open.Count - 1] != closed)
                {
                    throw new InvalidDataException($"'</{closed}>' does not close the element it is in");
                }

                open.RemoveAt(open.Count - 1);
                if (closed == "member" &&
                    InMembers(open))
                {
                    members.Add(new(memberStart, close, memberName));
                }

                position = close;
                continue;
            }

            var start = position;
            position = StartTag(xml, position, out var element, out var name, out var empty);
            var isMember = element == "member" &&
                           InMembers(open);
            if (empty)
            {
                if (isMember)
                {
                    members.Add(new(start, position, name));
                }

                continue;
            }

            if (isMember)
            {
                memberStart = start;
                memberName = name;
            }

            open.Add(element);
        }

        if (open.Count > 0)
        {
            throw new InvalidDataException($"'<{open[open.Count - 1]}>' is never closed");
        }

        return members;
    }

    static bool InMembers(List<string> open) =>
        open.Count == 2 &&
        open[0] == "doc" &&
        open[1] == "members";

    /// <summary>
    /// Past a byte order mark. The markup of UTF-8 is single bytes that appear nowhere inside a
    /// longer character, which is what lets the file be read without decoding it; a wider encoding
    /// has neither property.
    /// </summary>
    static int Start(byte[] xml)
    {
        if (xml.Length >= 3 &&
            xml[0] == 0xEF &&
            xml[1] == 0xBB &&
            xml[2] == 0xBF)
        {
            return 3;
        }

        if (xml.Length >= 2 &&
            (xml[0] == 0xFF && xml[1] == 0xFE ||
             xml[0] == 0xFE && xml[1] == 0xFF ||
             xml[0] == 0 ||
             xml[1] == 0))
        {
            throw new InvalidDataException("it is not UTF-8");
        }

        return 0;
    }

    /// <summary>
    /// Reads a start tag from its '&lt;'. Gives the element, its name attribute or "", whether the
    /// tag closes itself, and the position after it. Attribute values are read as quoted text, so a
    /// '&gt;' inside one does not end the tag.
    /// </summary>
    static int StartTag(byte[] xml, int position, out string element, out string name, out bool empty)
    {
        var at = position + 1;
        while (at < xml.Length &&
               !IsSpace(xml[at]) &&
               xml[at] != '>' &&
               xml[at] != '/')
        {
            at++;
        }

        element = Text(xml, position + 1, at);
        name = "";
        while (true)
        {
            at = SkipSpace(xml, at);
            if (at >= xml.Length)
            {
                throw new InvalidDataException($"the tag '<{element}' is never closed");
            }

            if (xml[at] == '>')
            {
                empty = false;
                return at + 1;
            }

            if (At(xml, at, "/>"))
            {
                empty = true;
                return at + 2;
            }

            var attributeStart = at;
            while (at < xml.Length &&
                   !IsSpace(xml[at]) &&
                   xml[at] != '=' &&
                   xml[at] != '>' &&
                   xml[at] != '/')
            {
                at++;
            }

            var attribute = Text(xml, attributeStart, at);
            at = SkipSpace(xml, at);
            if (attribute.Length == 0 ||
                at >= xml.Length ||
                xml[at] != '=')
            {
                throw new InvalidDataException($"the tag '<{element}' has an attribute with no value");
            }

            at = SkipSpace(xml, at + 1);
            if (at >= xml.Length ||
                xml[at] != '"' && xml[at] != '\'')
            {
                throw new InvalidDataException($"the tag '<{element}' has an attribute value that is not quoted");
            }

            var end = Array.IndexOf(xml, xml[at], at + 1);
            if (end < 0)
            {
                throw new InvalidDataException($"the tag '<{element}' has an attribute value that is never closed");
            }

            if (attribute == "name")
            {
                name = Text(xml, at + 1, end);
            }

            at = end + 1;
        }
    }

    /// <summary>
    /// The lines an element is on, where it has them to itself, so that taking it out leaves no
    /// blank line. Otherwise the element alone.
    /// </summary>
    static (int Start, int End) WholeLines(byte[] xml, int start, int end)
    {
        var lineStart = start;
        while (lineStart > 0 &&
               xml[lineStart - 1] is (byte)' ' or (byte)'\t')
        {
            lineStart--;
        }

        if (lineStart > 0 &&
            xml[lineStart - 1] != '\n' &&
            xml[lineStart - 1] != '\r')
        {
            return (start, end);
        }

        var lineEnd = end;
        while (lineEnd < xml.Length &&
               xml[lineEnd] is (byte)' ' or (byte)'\t')
        {
            lineEnd++;
        }

        if (lineEnd == xml.Length)
        {
            return (lineStart, lineEnd);
        }

        if (At(xml, lineEnd, "\r\n"))
        {
            return (lineStart, lineEnd + 2);
        }

        if (xml[lineEnd] is (byte)'\n' or (byte)'\r')
        {
            return (lineStart, lineEnd + 1);
        }

        return (start, end);
    }

    /// <summary>
    /// A name as it was before it was written as an attribute. The compiler names an extension
    /// block's types with angle brackets, which an attribute can only hold as entities. A name that
    /// cannot be read comes back empty, which is no member's name.
    /// </summary>
    static string Unescape(string value)
    {
        if (value.IndexOf('&') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var position = 0;
        while (position < value.Length)
        {
            if (value[position] != '&')
            {
                builder.Append(value[position]);
                position++;
                continue;
            }

            var end = value.IndexOf(';', position);
            if (end < 0 ||
                !Entity(value.Substring(position + 1, end - position - 1), out var text))
            {
                return "";
            }

            builder.Append(text);
            position = end + 1;
        }

        return builder.ToString();
    }

    static bool Entity(string entity, out string text)
    {
        text = entity switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "quot" => "\"",
            "apos" => "'",
            _ => ""
        };
        if (text.Length > 0)
        {
            return true;
        }

        var style = NumberStyles.None;
        var digits = entity.TrimStart('#');
        if (entity.StartsWith("#x", StringComparison.Ordinal))
        {
            style = NumberStyles.AllowHexSpecifier;
            digits = entity.Substring(2);
        }

        if (!entity.StartsWith("#", StringComparison.Ordinal) ||
            !int.TryParse(digits, style, CultureInfo.InvariantCulture, out var code) ||
            code is <= 0 or > 0x10FFFF or >= 0xD800 and <= 0xDFFF)
        {
            return false;
        }

        text = char.ConvertFromUtf32(code);
        return true;
    }

    static bool At(byte[] xml, int position, string text)
    {
        if (position + text.Length > xml.Length)
        {
            return false;
        }

        for (var index = 0; index < text.Length; index++)
        {
            if (xml[position + index] != text[index])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The position after the next occurrence of some text.
    /// </summary>
    static int After(byte[] xml, int position, string text)
    {
        for (; position < xml.Length; position++)
        {
            if (At(xml, position, text))
            {
                return position + text.Length;
            }
        }

        throw new InvalidDataException($"something is opened and '{text}' never closes it");
    }

    static int SkipSpace(byte[] xml, int position)
    {
        while (position < xml.Length &&
               IsSpace(xml[position]))
        {
            position++;
        }

        return position;
    }

    static bool IsSpace(byte value) =>
        value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

    static string Text(byte[] xml, int start, int end) =>
        Encoding.UTF8.GetString(xml, start, end - start);
}

/// <summary>
/// A documentation file after trimming, and how many entries were taken out of it. With none taken
/// out, the content is the array that went in.
/// </summary>
public sealed record TrimmedDocumentation(byte[] Content, int Removed);
