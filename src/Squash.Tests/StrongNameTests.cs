using System.Buffers.Binary;
using System.Reflection.Metadata.Ecma335;

public class StrongNameTests
{
    static string Fixture(string name, string framework) =>
        Path.Combine(TestEnvironment.Fixture(name, framework), name + ".dll");

    static byte[] PublicKey(string path) =>
        AssemblyName.GetAssemblyName(path).GetPublicKey()!;

    static byte[] Key => File.ReadAllBytes(Trims.KeyFile);

    static byte[] WithEmptySignature(byte[] image)
    {
        var copy = (byte[])image.Clone();
        var pe = PeFile.Read(copy);
        Array.Clear(copy, pe.SignatureOffset, pe.SignatureSize);
        return copy;
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    public async Task TheCompilersOwnSignatureVerifies(string framework)
    {
        // Nothing of Squash's signed this file. If the hash here were computed over anything but
        // what the compiler hashed, its signature would not verify.
        var path = Fixture("Signed", framework);
        await Assert.That(StrongName.Verify(await File.ReadAllBytesAsync(path), PublicKey(path))).IsTrue();
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    public async Task SigningAgainReproducesTheCompilersSignature(string framework)
    {
        // RSA PKCS#1 v1.5 has no randomness, so the same key over the same content gives the same
        // bytes: the whole file must come back identical.
        var path = Fixture("Signed", framework);
        var original = await File.ReadAllBytesAsync(path);
        var image = WithEmptySignature(original);
        await Assert.That(StrongName.Verify(image, PublicKey(path))).IsFalse();

        StrongName.Sign(image, PublicKey(path), Key);

        await Assert.That(image.SequenceEqual(original)).IsTrue();
    }

    [Test]
    public async Task ATamperedImageDoesNotVerify()
    {
        var path = Fixture("Signed", "netstandard2.0");
        var image = await File.ReadAllBytesAsync(path);
        image[^1] ^= 0xFF;
        await Assert.That(StrongName.Verify(image, PublicKey(path))).IsFalse();
    }

    [Test]
    public async Task OnlyARealSignatureCountsAsSigned()
    {
        await Assert.That(StrongName.IsSigned(await File.ReadAllBytesAsync(Fixture("Signed", "netstandard2.0")))).IsTrue();

        // Public-signed: the flag is set and the signature is zeros.
        await Assert.That(StrongName.IsSigned(await File.ReadAllBytesAsync(Fixture("PublicSigned", "netstandard2.0")))).IsFalse();
        await Assert.That(StrongName.IsSigned(await File.ReadAllBytesAsync(Fixture("Scenarios", "netstandard2.0")))).IsFalse();
    }

    [Test]
    public async Task AnotherKeyIsRejected()
    {
        var path = Fixture("Signed", "netstandard2.0");
        using var other = new RSACryptoServiceProvider(2048);
        var image = WithEmptySignature(await File.ReadAllBytesAsync(path));

        var exception = Assert.Throws<SquashException>(() => StrongName.Sign(image, PublicKey(path), other.ExportCspBlob(true)));

        await Assert.That(exception.Code).IsEqualTo(Diagnostics.CannotResign);
    }

    [Test]
    public async Task APublicKeyAloneCannotSign()
    {
        var path = Fixture("Signed", "netstandard2.0");
        using var rsa = new RSACryptoServiceProvider();
        rsa.ImportCspBlob(Key);
        var image = WithEmptySignature(await File.ReadAllBytesAsync(path));

        var exception = Assert.Throws<SquashException>(() => StrongName.Sign(image, PublicKey(path), rsa.ExportCspBlob(false)));

        await Assert.That(exception.Code).IsEqualTo(Diagnostics.CannotResign);
    }

    [Test]
    [Arguments("Signed", "netstandard2.0")]
    [Arguments("Signed", "net48")]
    [Arguments("Scenarios", "net10.0")]
    public async Task ThePeReaderAgreesWithSystemReflectionMetadata(string name, string framework) =>
        await AgreesWithSystemReflectionMetadata(await File.ReadAllBytesAsync(Fixture(name, framework)), PEMagic.PE32);

    [Test]
    public Task ThePeReaderAgreesWithSystemReflectionMetadataOnPe32Plus() =>
        AgreesWithSystemReflectionMetadata(Pe32PlusImage(), PEMagic.PE32Plus);

    static async Task AgreesWithSystemReflectionMetadata(byte[] image, PEMagic magic)
    {
        var mine = PeFile.Read(image);

        using var pe = new PEReader(new MemoryStream(image));
        var headers = pe.PEHeaders;
        var cor = headers.CorHeader!;

        await Assert.That(headers.PEHeader!.Magic).IsEqualTo(magic);
        await Assert.That(BitConverter.ToUInt32(image, mine.CheckSumOffset)).IsEqualTo(headers.PEHeader.CheckSum);
        await Assert.That(BitConverter.ToInt32(image, mine.CertificateEntryOffset)).IsEqualTo(headers.PEHeader.CertificateTableDirectory.RelativeVirtualAddress);
        await Assert.That(BitConverter.ToInt32(image, mine.FlagsOffset)).IsEqualTo((int)cor.Flags);
        await Assert.That(mine.SignatureSize).IsEqualTo(cor.StrongNameSignatureDirectory.Size);
        await Assert.That(mine.Sections.Count).IsEqualTo(headers.SectionHeaders.Length);
        await Assert.That(mine.Sections[0].RawPointer).IsEqualTo(headers.SectionHeaders[0].PointerToRawData);

        // The headers stop where the section table does, and the first section starts after them.
        await Assert.That(mine.HeadersEnd).IsLessThanOrEqualTo(headers.SectionHeaders[0].PointerToRawData);
        await Assert.That(mine.HeadersEnd).IsGreaterThan(headers.PEHeaderStartOffset);

        if (cor.StrongNameSignatureDirectory.Size > 0)
        {
            headers.TryGetDirectoryOffset(cor.StrongNameSignatureDirectory, out var offset);
            await Assert.That(mine.SignatureOffset).IsEqualTo(offset);
        }
    }

    [Test]
    public void NotAnAssembly()
    {
        Assert.Throws<BadImageFormatException>(() => PeFile.Read(new byte[100]));
        Assert.Throws<BadImageFormatException>(() => PeFile.Read("MZ, and then nothing that follows the format at all, padded to length"u8.ToArray()));
    }

    [Test]
    public Task AFileShorterThanADosHeader() =>
        Rejects(DosHeader(0x3F), "Not a PE file.");

    [Test]
    public Task OnlyHalfTheDosSignature()
    {
        var image = DosHeader(100);
        image[1] = 0;
        return Rejects(image, "Not a PE file.");
    }

    [Test]
    public Task NoPeSignatureWhereTheDosHeaderPoints()
    {
        var image = DosHeader(0x80);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(0x3C), 0x40);
        return Rejects(image, "Not a PE file.");
    }

    [Test]
    public Task AnUnknownOptionalHeader()
    {
        var image = SignedImage();
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(MagicOffset(image)), 0x1234);
        return Rejects(image, "Unknown PE optional header.");
    }

    [Test]
    public Task ANativeImage()
    {
        var image = SignedImage();
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(CliEntryOffset(image)), 0);
        return Rejects(image, "Not a managed assembly.");
    }

    [Test]
    public Task ACliHeaderOutsideEverySection()
    {
        var image = SignedImage();
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(CliEntryOffset(image)), 0x70000000);
        return Rejects(image, "An address in the PE headers is outside every section.");
    }

    static async Task Rejects(byte[] image, string message)
    {
        var exception = Assert.Throws<BadImageFormatException>(() => PeFile.Read(image));

        await Assert.That(exception.Message).IsEqualTo(message);

        // Turned away by a check, not by a read that ran off the end of the file.
        await Assert.That(exception.InnerException).IsNull();
    }

    static byte[] DosHeader(int length)
    {
        var image = new byte[length];
        image[0] = (byte)'M';
        image[1] = (byte)'Z';
        return image;
    }

    static byte[] SignedImage() =>
        File.ReadAllBytes(Fixture("Signed", "netstandard2.0"));

    // The optional header opens with its magic number, 64 bytes before the CheckSum field.
    static int MagicOffset(byte[] image) =>
        PeFile.Read(image).CheckSumOffset - 64;

    // The CLI header's data directory entry: ten entries of 8 bytes after the certificate table's.
    static int CliEntryOffset(byte[] image) =>
        PeFile.Read(image).CertificateEntryOffset + 10 * 8;

    // No fixture is built for a 64-bit platform, so a managed PE32+ image is written here.
    static byte[] Pe32PlusImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("Pe32Plus.dll"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("Pe32Plus"), new(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.None);

        var builder = new ManagedPEBuilder(
            new(machine: Machine.Amd64),
            new(metadata),
            ilStream: new());
        var blob = new BlobBuilder();
        builder.Serialize(blob);
        return blob.ToArray();
    }
}
