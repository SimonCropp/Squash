/// <summary>
/// A project that the library names in InternalsVisibleTo, building against the trimmed library.
/// </summary>
public class FriendTests
{
    static readonly string project = Path.Combine("Friend", "Friend.csproj");

    [Test]
    public async Task AFriendUsingAnInternalThatSurvivedBuilds()
    {
        var result = await Consumer.Build("Library", project);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        // The library's build says which friends it disregarded.
        await Assert.That(result.Cli.Combined).Contains("Squash008");
        await Assert.That(result.Cli.Combined).Contains("only Friend would use");
    }

    [Test]
    public async Task AFriendUsingARemovedInternalFailsToCompile()
    {
        var result = await Consumer.Build(
            "Library",
            project,
            new Dictionary<string, string>
            {
                ["UseRemoved"] = "true"
            });

        // The compiler's reference assembly for Lib still lists OnlyForFriends. Compiled against
        // that, this would build and then fail when the method was first called. Squash has the
        // friend compile against the trimmed assembly, so it is the compiler that objects.
        await Assert.That(result.Cli.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Cli.Combined).Contains("error CS0117");
        await Assert.That(result.Cli.Combined).Contains("OnlyForFriends");
    }

    [Test]
    public async Task HonoringFriendsKeepsWhatTheyUse()
    {
        var result = await Consumer.Build(
            "Library",
            project,
            new Dictionary<string, string>
            {
                ["UseRemoved"] = "true",
                ["SquashInternalsVisibleTo"] = "Honor"
            });

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(result.Cli.Combined).DoesNotContain("Squash008");
    }

    [Test]
    public async Task FriendRootsKeepWhatAFriendUses()
    {
        var uses = new Dictionary<string, string>
        {
            ["UseRemoved"] = "true",
            ["UseMore"] = "true"
        };

        // The friend is read as a compiled assembly, and it only compiles against a library that
        // still has its internals.
        var honored = await Consumer.Build(
            "Library",
            project,
            new Dictionary<string, string>(uses)
            {
                ["SquashInternalsVisibleTo"] = "Honor"
            });
        await Assert.That(honored.Cli.ExitCode).IsEqualTo(0).Because(honored.Cli.Combined);

        var written = await Consumer.Rebuild(
            honored.Work,
            Path.Combine("Lib", "Lib.csproj"),
            new Dictionary<string, string>
            {
                ["SquashFriendRoots"] = "Update"
            });
        await Assert.That(written.Cli.ExitCode).IsEqualTo(0).Because(written.Cli.Combined);
        await Assert.That(written.Cli.Combined).Contains("from 1 friend assemblies");
        await Assert.That(File.Exists(Path.Combine(honored.Work, "Lib", "FriendRoots", "netstandard2.0.xml"))).IsTrue();

        // Friends ignored again, as they are by default: the roots are what lets this compile.
        var result = await Consumer.Rebuild(honored.Work, project, uses);
        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);

        // Kept for the friend is not kept whole.
        await Assert.That(Consumer.HasType(result.Output("Friend", "net10.0", "Lib.dll"), "Lib.UnusedInternal")).IsFalse();
        var removed = await File.ReadAllTextAsync(result.Squash("Lib", "net10.0", "removed.txt"));
        await Assert.That(removed).Contains("Lib.Constants::Unused()");
        await Assert.That(removed).Contains("Lib.Visible::Unused()");
        await Assert.That(removed).Contains("Lib.Holder`1::Unused()");

        await Verify(await File.ReadAllTextAsync(Path.Combine(honored.Work, "Lib", "FriendRoots", "net10.0.xml")));
    }

    [Test]
    public async Task ALibraryWithoutFriendsKeepsItsReferenceAssembly()
    {
        // Compiling dependents against the implementation costs them a rebuild whenever the library
        // changes at all. That is only paid where friends were ignored.
        var work = Consumer.Prepare("Library");
        var source = Path.Combine(work, "Lib", "Api.cs");
        await File.WriteAllTextAsync(source, (await File.ReadAllTextAsync(source)).Replace("[assembly: InternalsVisibleTo(\"Friend\")]", ""));

        var result = await Consumer.Rebuild(work, Path.Combine("App", "App.csproj"), arguments: ["-v:normal"]);

        await Assert.That(result.Cli.ExitCode).IsEqualTo(0).Because(result.Cli.Combined);
        await Assert.That(File.Exists(result.Squash("Lib", "net10.0", "friends.txt"))).IsFalse();
        await Assert.That(result.Cli.Stdout).Contains(Path.Combine("Lib", "obj", "Release", "net10.0", "ref", "Lib.dll"));
    }
}
