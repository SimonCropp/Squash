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
    public async Task ThePeReaderAgreesWithSystemReflectionMetadata(string name, string framework)
    {
        var path = Fixture(name, framework);
        var image = await File.ReadAllBytesAsync(path);
        var mine = PeFile.Read(image);

        await using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var headers = pe.PEHeaders;
        var cor = headers.CorHeader!;

        await Assert.That(BitConverter.ToUInt32(image, mine.CheckSumOffset)).IsEqualTo(headers.PEHeader!.CheckSum);
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
}
