/// <summary>
/// What must hold for any assembly Squash rewrites, whatever the linker chose to remove. These are
/// the checks that make it safe to take a new linker without a person reading its output.
/// </summary>
public class ValidityTests
{
    public static IEnumerable<(string Variant, string Framework)> Runs() =>
        Trims.All();

    [Test]
    [MethodDataSource(nameof(Runs))]
    public async Task TheVisibleSurfaceIsUntouched(string variant, string framework)
    {
        var trimmed = Trims.Get(variant, framework);

        await Assert.That(trimmed.Succeeded).IsTrue();
        await Assert.That(AssemblyDump.Surface(trimmed.Assembly)).IsEqualTo(AssemblyDump.Surface(trimmed.Original));
    }

    [Test]
    [MethodDataSource(nameof(Runs))]
    public async Task SurvivingMethodBodiesAreTheSameSize(string variant, string framework)
    {
        // Squash asks the linker to remove members, never to rewrite what stays. A body that changed
        // size was rewritten.
        var trimmed = Trims.Get(variant, framework);
        var before = AssemblyDump.BodySizes(trimmed.Original);
        foreach (var (method, size) in AssemblyDump.BodySizes(trimmed.Assembly))
        {
            await Assert.That(size).IsEqualTo(before[method]).Because(method);
        }
    }

    [Test]
    [MethodDataSource(nameof(Runs))]
    public async Task TheResultIsValidIl(string variant, string framework)
    {
        var trimmed = Trims.Get(variant, framework);
        var before = Checks.IlErrors(trimmed.Original, trimmed.References);
        var after = Checks.IlErrors(trimmed.Assembly, trimmed.References);

        await Assert.That(after.Except(before)).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Runs))]
    public async Task EveryMethodStillCompiles(string variant, string framework)
    {
        var trimmed = Trims.Get(variant, framework);
        var failures = Checks.JitFailures(trimmed.Assembly, TestEnvironment.Fixture(trimmed.Name, trimmed.Framework));

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(Runs))]
    public async Task TheSymbolsBelongToTheAssembly(string variant, string framework)
    {
        var trimmed = Trims.Get(variant, framework);
        var facts = Checks.Symbols(trimmed.Assembly);

        await Assert.That(facts.SymbolsId).IsEqualTo(facts.AssemblyId);

        // One row for each method that is left, so a debugger maps the right source to each.
        await Assert.That(facts.MethodsWithSymbols).IsEqualTo(facts.Methods);
        await Assert.That(facts.Embedded).IsEqualTo(Checks.Symbols(trimmed.Original).Embedded);
    }

    [Test]
    public async Task TheIlCheckNoticesABrokenBody()
    {
        // A check that never fails proves nothing. Replace the ret that ends a method with a pop,
        // and the verifier has to object.
        using var temp = new TempDirectory();
        var trimmed = Trims.Scenarios("netstandard2.0");
        var broken = temp.Combine("Scenarios.dll");
        var image = await File.ReadAllBytesAsync(trimmed.Assembly);
        var body = MethodBody(trimmed.Assembly, "Helper", "Used");
        var at = IndexOf(image, body);
        image[at + body.Length - 1] = 0x26;
        await File.WriteAllBytesAsync(broken, image);

        await Assert.That(Checks.IlErrors(broken, trimmed.References)).IsNotEmpty();
    }

    [Test]
    public async Task TheCompileCheckNoticesAMissingDependency()
    {
        // The same for the other check: with the dependency out of reach, the methods that use it
        // cannot be compiled.
        using var temp = new TempDirectory();
        var trimmed = Trims.Scenarios("netstandard2.0");

        await Assert.That(Checks.JitFailures(trimmed.Assembly, temp.Path)).IsNotEmpty();
    }

    static byte[] MethodBody(string assembly, string type, string method)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var definition = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Single(_ => reader.GetString(_.Name) == type)
            .GetMethods()
            .Select(reader.GetMethodDefinition)
            .Single(_ => reader.GetString(_.Name) == method);
        return pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
    }

    static int IndexOf(byte[] image, byte[] pattern)
    {
        for (var index = 0; index <= image.Length - pattern.Length; index++)
        {
            if (image.AsSpan(index, pattern.Length).SequenceEqual(pattern))
            {
                return index;
            }
        }

        throw new("The method body was not found in the file.");
    }

    [Test]
    [Arguments("netstandard2.0")]
    [Arguments("net48")]
    [Arguments("net10.0")]
    public async Task TheSameInputGivesTheSameBytes(string framework)
    {
        // Two separate runs in two separate directories.
        using var first = Trimmed.Run("Scenarios", framework);
        using var second = Trimmed.Run("Scenarios", framework);

        var firstAssembly = await File.ReadAllBytesAsync(first.Assembly);
        var secondAssembly = await File.ReadAllBytesAsync(second.Assembly);
        var firstSymbols = await File.ReadAllBytesAsync(first.Symbols);
        var secondSymbols = await File.ReadAllBytesAsync(second.Symbols);

        await Assert.That(firstAssembly.SequenceEqual(secondAssembly)).IsTrue();
        await Assert.That(firstSymbols.SequenceEqual(secondSymbols)).IsTrue();
    }
}
