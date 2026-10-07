namespace Squash;

/// <summary>
/// Everything that decides the linker's command line, and nothing else.
/// </summary>
public sealed class SquashRequest
{
    public string AssemblyName { get; init; } = "";
    public string Input { get; init; } = "";
    public IReadOnlyList<string> References { get; init; } = [];
    public IReadOnlyList<string> RootDescriptors { get; init; } = [];
    public string OutputDirectory { get; init; } = "";
    public string StepsAssembly { get; init; } = "";
    public string FriendsFile { get; init; } = "";
    public string ReportFile { get; init; } = "";
    public bool IgnoreInternalsVisibleTo { get; init; }
    public bool KeepDataShape { get; init; }
    public bool WarningsAsErrors { get; init; }
    public bool RootEntryPoint { get; init; }
    public string ExtraArgs { get; init; } = "";
}
