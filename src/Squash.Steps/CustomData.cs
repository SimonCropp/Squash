/// <summary>
/// The --custom-data keys the task passes. The task assembly cannot reference this one (it is
/// netstandard2.0 and never loads the linker), so ResponseFile repeats these strings; the behaviour
/// tests fail if the two drift.
/// </summary>
static class CustomData
{
    public const string RootAssembly = "Squash.RootAssembly";
    public const string FriendsFile = "Squash.FriendsFile";
    public const string ReportFile = "Squash.ReportFile";
    public const string AssemblyFile = "Squash.AssemblyFile";
    public const string RemovedDocumentationFile = "Squash.RemovedDocumentationFile";

    // Where, under the directory the task started the linker in, the assembly it is given is and
    // where the linker is told to write. SquashTask repeats these.
    public const string InputDirectory = "in";
    public const string OutputDirectory = "out";

    public static string? Get(LinkContext context, string key)
    {
        if (context.TryGetCustomData(key, out var value) &&
            value.Length > 0)
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// A file the task asked for. The value is a bare file name, resolved against the directory the
    /// task started the linker in: the linker splits custom data on every '=', so a full path
    /// containing one would be rejected.
    /// </summary>
    public static string? File(LinkContext context, string key)
    {
        var name = Get(context, key);
        if (name == null)
        {
            return null;
        }

        return Path.GetFullPath(name);
    }
}
