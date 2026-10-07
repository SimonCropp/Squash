public static class CliRunner
{
    public static async Task<CliResult> Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? packagesDirectory = null,
        Cancel cancellation = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            Environment =
            {
                ["DOTNET_NOLOGO"] = "true",
                ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "true",
                // English, so the tests can look for a compiler error by its text.
                ["DOTNET_CLI_UI_LANGUAGE"] = "en"
            }
        };
        if (packagesDirectory != null)
        {
            // Without an isolated package directory the global cache serves a previously restored
            // build of the same version, and a rebuilt package silently never reaches the fixture.
            info.Environment["NUGET_PACKAGES"] = packagesDirectory;
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ??
                            throw new($"Could not start {executable}.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
        var stderr = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        return new(process.ExitCode, await stdout, await stderr);
    }
}
