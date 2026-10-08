/// <summary>
/// Lists, by the name documentation knows it by, every type and member that went into the linker and
/// did not come out. The task takes those entries out of the documentation file. Runs after the
/// output is written and reads only the two files, so the list is what was removed and never what
/// was expected to be: a name still in the output is not on it, whatever else shares that name. In
/// the global namespace so --custom-step can name it unqualified.
/// </summary>
public sealed class RemovedDocumentation :
    BaseStep
{
    protected override void Process()
    {
        var file = CustomData.File(Context, CustomData.RemovedDocumentationFile);
        var assembly = CustomData.Get(Context, CustomData.AssemblyFile);
        if (file == null ||
            assembly == null)
        {
            return;
        }

        var output = Path.GetFullPath(Path.Combine(CustomData.OutputDirectory, assembly));

        // Nothing was reachable, which the task reports.
        if (!File.Exists(output))
        {
            return;
        }

        try
        {
            var removed = DocumentationIds.Read(Path.GetFullPath(Path.Combine(CustomData.InputDirectory, assembly)));
            removed.ExceptWith(DocumentationIds.Read(output));

            // "\n" on every platform, so the file is the same wherever the build ran.
            File.WriteAllText(file, string.Concat(removed.Order(StringComparer.Ordinal).Select(_ => _ + "\n")));
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Squash could not work out which documentation belongs to what was removed from '{assembly}'. Set Squash_TrimDocumentation to false to leave the documentation file as the compiler wrote it.",
                exception);
        }
    }
}
