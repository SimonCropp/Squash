public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public string Combined => Stdout + Stderr;
}
