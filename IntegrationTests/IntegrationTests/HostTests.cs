/// <summary>
/// The same package under the other host: MSBuild on .NET Framework, as Visual Studio and
/// msbuild.exe run it. The task there is the same netstandard2.0 assembly, and the linker still
/// runs on .NET in a process of its own.
/// </summary>
public class HostTests
{
    [Test]
    public async Task MsBuildExeTrims()
    {
        var msbuild = FindMsBuild();
        if (msbuild == null)
        {
            // Not Windows, or no Visual Studio new enough to build with this SDK.
            return;
        }

        var work = Consumer.Prepare("Library");
        List<string> arguments =
        [
            Path.Combine(work, "Lib", "Lib.csproj"),
            "-restore",
            "-nologo",
            "-nodeReuse:false",
            "-verbosity:minimal",
            "-p:Configuration=Release",
            .. Consumer.Properties(null)
        ];
        var cli = await CliRunner.Run(msbuild, arguments, work, Consumer.PackagesDirectory);
        var result = new BuildResult(cli, work);

        await Assert.That(cli.ExitCode).IsEqualTo(0).Because(cli.Combined);
        foreach (var framework in new[] { "netstandard2.0", "net10.0" })
        {
            await Assert.That(Consumer.HasType(result.Output("Lib", framework, "Lib.dll"), "Lib.UnusedInternal")).IsFalse();
        }
    }

    /// <summary>
    /// The newest msbuild.exe that vswhere knows, if it is at least the version the pinned SDK needs.
    /// </summary>
    static string? FindMsBuild()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var vswhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            return null;
        }

        var info = new ProcessStartInfo(vswhere)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-latest", "-prerelease", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe" })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var path = process.StandardOutput
            .ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        process.WaitForExit();

        // The .NET 10 SDK needs MSBuild 18.
        if (path == null ||
            !File.Exists(path) ||
            FileVersionInfo.GetVersionInfo(path).FileMajorPart < 18)
        {
            return null;
        }

        return path;
    }
}
