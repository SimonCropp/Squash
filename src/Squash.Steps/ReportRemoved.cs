/// <summary>
/// Lists every type, method and field the linker is about to remove from the root assembly. Removal
/// is otherwise silent, and a member reached only by reflection disappears the same way as a dead
/// one; the list is what makes a build auditable. Runs once marking is final and before the sweep.
/// In the global namespace so --custom-step can name it unqualified.
/// </summary>
public sealed class ReportRemoved :
    BaseStep
{
    protected override void Process()
    {
        var file = CustomData.File(Context, CustomData.ReportFile);
        var assembly = RootAssembly.Loaded(Context);
        if (file == null ||
            assembly == null)
        {
            return;
        }

        var removed = new List<string>();
        foreach (var type in assembly.MainModule.Types)
        {
            Collect(type, removed);
        }

        removed.Sort(StringComparer.Ordinal);

        // "\n" on every platform, so the file is the same wherever the build ran.
        File.WriteAllText(file, string.Concat(removed.Select(_ => _ + "\n")));
    }

    void Collect(TypeDefinition type, List<string> removed)
    {
        // The module type holds no members of its own to lose and is never marked.
        if (type.Name == "<Module>")
        {
            return;
        }

        if (!Annotations.IsMarked(type))
        {
            // Its members and nested types go with it; listing them too would bury the line that
            // matters.
            removed.Add($"type {type.FullName}");
            return;
        }

        removed.AddRange(
            type.Fields
                .Where(_ => !Annotations.IsMarked(_))
                .Select(_ => $"field {_.FullName}"));
        removed.AddRange(
            type.Methods
                .Where(_ => !Annotations.IsMarked(_))
                .Select(_ => $"method {_.FullName}"));

        foreach (var nested in type.NestedTypes)
        {
            Collect(nested, removed);
        }
    }
}
