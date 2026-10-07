public class NamespacesTests
{
    /// <summary>
    /// What a real metadata reader says of the same file.
    /// </summary>
    static List<string>? Expected(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            return null;
        }

        var reader = pe.GetMetadataReader();
        return reader.TypeDefinitions
            .Select(_ => reader.GetString(reader.GetTypeDefinition(_).Namespace))
            .Where(_ => _.Length > 0)
            .Distinct()
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();
    }

    static IEnumerable<string> Assemblies()
    {
        // The framework: small and large string heaps, and tables with few rows and with many.
        foreach (var file in Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
        {
            yield return file;
        }

        // The compiler, which has more methods than a two byte index can hold.
        var sdk = Path.Combine(Path.GetDirectoryName(TestEnvironment.DotNetHost)!, "sdk");
        foreach (var file in Directory.EnumerateFiles(sdk, "Microsoft.CodeAnalysis*.dll", SearchOption.AllDirectories))
        {
            yield return file;
        }

        foreach (var framework in Trims.Frameworks)
        {
            yield return Path.Combine(TestEnvironment.Fixture("Scenarios", framework), "Scenarios.dll");
        }
    }

    [Test]
    public async Task ReadsWhatAMetadataReaderReads()
    {
        var compared = 0;
        var wide = 0;
        foreach (var path in Assemblies())
        {
            var expected = Expected(path);
            if (expected == null)
            {
                // Native.
                continue;
            }

            var image = await File.ReadAllBytesAsync(path);
            await Assert.That(Namespaces.Read(image).SequenceEqual(expected)).IsTrue().Because(path);
            compared++;
            if (image.Length > 4_000_000)
            {
                wide++;
            }
        }

        // Enough of them, and some large enough to need the wider indexes.
        await Assert.That(compared).IsGreaterThan(100);
        await Assert.That(wide).IsGreaterThan(0);
    }

    [Test]
    public async Task TheGlobalNamespaceAndNestedTypesAddNothing()
    {
        var path = Path.Combine(TestEnvironment.Fixture("Scenarios", "net10.0"), "Scenarios.dll");
        var namespaces = Namespaces.Read(await File.ReadAllBytesAsync(path));

        await Assert.That(namespaces).Contains("Scenarios");
        await Assert.That(namespaces).DoesNotContain("");
    }

    [Test]
    public async Task NotAnAssembly() =>
        await Assert.That(() => Namespaces.Read(new byte[100])).Throws<BadImageFormatException>();

    [Test]
    public async Task PrefixesAreSplitAndTidied()
    {
        var prefixes = Namespaces.Prefixes(" Mine;Mine.Other* ,\n  Theirs;;Mine ");

        await Assert.That(prefixes.SequenceEqual(["Mine", "Mine.Other", "Theirs"])).IsTrue().Because(string.Join("|", prefixes));
        await Assert.That(Namespaces.Prefixes("")).IsEmpty();
    }

    [Test]
    public async Task ANamespaceMatchesByPrefix()
    {
        string[] declared = ["Mine", "MineToo", "Mine.Deeper", "NotMine", "System.Mine"];
        var matched = Namespaces.Matching(declared, ["Mine"]);

        // The start of the name, not a whole segment of it and not anywhere within it.
        await Assert.That(matched.SequenceEqual(["Mine", "MineToo", "Mine.Deeper"])).IsTrue().Because(string.Join("|", matched));
    }

    [Test]
    public async Task TheDescriptorKeepsEachNamespace()
    {
        var descriptor = Namespaces.Descriptor("My&Lib", ["Mine", "Mine.Deeper"]);

        await Assert.That(descriptor).IsEqualTo(
            """
            <linker>
              <assembly fullname="My&amp;Lib">
                <namespace fullname="Mine" />
                <namespace fullname="Mine.Deeper" />
              </assembly>
            </linker>

            """.Replace("\r\n", "\n"));
    }

    [Test]
    public async Task NoDescriptorForNoNamespaces() =>
        // An assembly element with nothing in it keeps the whole assembly.
        await Assert.That(() => Namespaces.Descriptor("Lib", [])).Throws<ArgumentException>();
}
