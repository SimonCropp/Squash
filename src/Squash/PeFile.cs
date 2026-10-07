namespace Squash;

/// <summary>
/// The few PE and CLI header fields strong-name signing needs, as offsets into the file. Read by
/// hand: the task assembly has no dependencies, and System.Reflection.Metadata is not part of
/// netstandard2.0.
/// </summary>
public sealed class PeFile
{
    // Set in the CLI header's flags when the image carries, or claims to carry, a strong name.
    public const int StrongNameSignedFlag = 0x8;

    const int peSignature = 0x00004550;
    const int certificateDirectoryIndex = 4;
    const int cliHeaderDirectoryIndex = 14;

    /// <summary>Where the headers stop: the end of the section table.</summary>
    public int HeadersEnd { get; private init; }

    /// <summary>The optional header's CheckSum field.</summary>
    public int CheckSumOffset { get; private init; }

    /// <summary>The data directory entry for the Authenticode certificate table.</summary>
    public int CertificateEntryOffset { get; private init; }

    /// <summary>The CLI header's Flags field.</summary>
    public int FlagsOffset { get; private init; }

    /// <summary>The strong-name signature slot. Size is 0 when the image has none.</summary>
    public int SignatureOffset { get; private init; }

    public int SignatureSize { get; private init; }

    public IReadOnlyList<PeSection> Sections { get; private init; } = [];

    public static PeFile Read(byte[] image)
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
            // An offset in the headers pointed outside the file.
            throw new BadImageFormatException("Not a PE file.", exception);
        }
    }

    static PeFile Parse(byte[] image)
    {
        if (image.Length < 0x40 ||
            image[0] != 'M' ||
            image[1] != 'Z')
        {
            throw new BadImageFormatException("Not a PE file.");
        }

        var nt = Int32(image, 0x3C);
        if (Int32(image, nt) != peSignature)
        {
            throw new BadImageFormatException("Not a PE file.");
        }

        var coff = nt + 4;
        var sectionCount = UInt16(image, coff + 2);
        var optionalSize = UInt16(image, coff + 16);
        var optional = coff + 20;

        // PE32 and PE32+ lay out the fields before the data directories differently.
        int directories;
        var magic = UInt16(image, optional);
        if (magic == 0x10B)
        {
            directories = optional + 96;
        }
        else if (magic == 0x20B)
        {
            directories = optional + 112;
        }
        else
        {
            throw new BadImageFormatException("Unknown PE optional header.");
        }

        var sectionTable = optional + optionalSize;
        var sections = new List<PeSection>();
        for (var index = 0; index < sectionCount; index++)
        {
            var header = sectionTable + index * 40;
            sections.Add(
                new(
                    RawPointer: Int32(image, header + 20),
                    RawSize: Int32(image, header + 16),
                    VirtualAddress: Int32(image, header + 12),
                    VirtualSize: Int32(image, header + 8)));
        }

        var cliRva = Int32(image, directories + cliHeaderDirectoryIndex * 8);
        if (cliRva == 0)
        {
            throw new BadImageFormatException("Not a managed assembly.");
        }

        var cli = Offset(sections, cliRva);
        var signatureRva = Int32(image, cli + 32);
        var signatureSize = Int32(image, cli + 36);
        var signatureOffset = 0;
        if (signatureSize > 0)
        {
            signatureOffset = Offset(sections, signatureRva);
        }

        return new()
        {
            HeadersEnd = sectionTable + sectionCount * 40,
            CheckSumOffset = optional + 64,
            CertificateEntryOffset = directories + certificateDirectoryIndex * 8,
            FlagsOffset = cli + 16,
            SignatureOffset = signatureOffset,
            SignatureSize = signatureSize,
            Sections = sections
        };
    }

    static int Offset(List<PeSection> sections, int rva)
    {
        foreach (var section in sections)
        {
            var size = Math.Max(section.VirtualSize, section.RawSize);
            if (rva >= section.VirtualAddress &&
                rva < section.VirtualAddress + size)
            {
                return rva - section.VirtualAddress + section.RawPointer;
            }
        }

        throw new BadImageFormatException("An address in the PE headers is outside every section.");
    }

    public static int Int32(byte[] bytes, int offset) =>
        bytes[offset] |
        bytes[offset + 1] << 8 |
        bytes[offset + 2] << 16 |
        bytes[offset + 3] << 24;

    static int UInt16(byte[] bytes, int offset) =>
        bytes[offset] |
        bytes[offset + 1] << 8;
}
