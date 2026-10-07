/// <summary>
/// Takes the InternalsVisibleTo attributes off the root assembly before the linker's own root step
/// runs. That step keeps every internal type and member as soon as it finds one such attribute; with
/// none to find, it roots only the visible surface. RestoreInternalsVisibleTo puts them back before
/// marking, so the output still declares its friends. In the global namespace so --custom-step can
/// name it unqualified.
/// </summary>
public sealed class HideInternalsVisibleTo :
    BaseStep
{
    protected override void Process()
    {
        var assembly = RootAssembly.Resolve(Context);
        if (assembly == null)
        {
            return;
        }

        HiddenFriends.Hide(assembly);

        var names = HiddenFriends.Names();
        var file = CustomData.File(Context, CustomData.FriendsFile);
        if (file == null ||
            names.Count == 0)
        {
            return;
        }

        // Its existence is the signal: the build then compiles dependents against the trimmed
        // assembly, and the task names these in its summary.
        File.WriteAllText(file, string.Join("\n", names) + "\n");
    }
}
