/// <summary>
/// Writes a root descriptor naming what the friend assemblies use of the root assembly, where the
/// visible surface would not keep it anyway. A friend is an assembly in the list the task wrote
/// whose name the root assembly gives in InternalsVisibleTo. Nothing is marked here: the descriptor
/// is a file for the linker's own root handling to read on later runs. In the global namespace so
/// --custom-step can name it unqualified.
/// </summary>
public sealed class CollectFriendRoots :
    BaseStep
{
    protected override void Process()
    {
        var library = RootAssembly.Resolve(Context);
        var list = CustomData.File(Context, CustomData.FriendAssembliesFile);
        var output = CustomData.File(Context, CustomData.FriendRootsFile);
        if (library == null ||
            list == null ||
            output == null)
        {
            return;
        }

        var names = library.CustomAttributes
            .Where(HiddenFriends.IsInternalsVisibleTo)
            .Select(HiddenFriends.Name)
            .Where(_ => _.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var roots = new FriendRoots(library);
        var parameters = new ReaderParameters
        {
            AssemblyResolver = new LibraryResolver(library)
        };
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builds = new HashSet<Guid>();
        var summary = new List<string>();
        foreach (var path in File.ReadAllLines(list))
        {
            // Cheaper than opening every assembly the build has produced.
            if (!names.Contains(Path.GetFileNameWithoutExtension(path)))
            {
                continue;
            }

            using var friend = Read(path, parameters);
            if (friend == null ||
                !names.Contains(friend.Name.Name))
            {
                continue;
            }

            found.Add(friend.Name.Name);

            // One build of a friend is copied into the output of everything that references it.
            if (!builds.Add(friend.MainModule.Mvid))
            {
                continue;
            }

            roots.Add(friend.MainModule);
            summary.Add($"read\t{path}");
        }

        summary.AddRange(
            names
                .Where(_ => !found.Contains(_))
                .OrderBy(_ => _, StringComparer.Ordinal)
                .Select(_ => $"missing\t{_}"));

        // "\n" on every platform, so the file is the same wherever the build ran.
        File.WriteAllText(output, roots.ToXml());
        File.WriteAllText(Path.ChangeExtension(output, ".txt"), string.Concat(summary.Select(_ => _ + "\n")));
    }

    static AssemblyDefinition? Read(string path, ReaderParameters parameters)
    {
        try
        {
            return AssemblyDefinition.ReadAssembly(path, parameters);
        }
        catch (BadImageFormatException)
        {
            // The native host of an application: the friend's name, and none of its metadata.
            return null;
        }
    }

    /// <summary>
    /// A friend's references to the root assembly resolve to the assembly this run loaded, not to
    /// whichever copy sits beside the friend. Nothing else needs resolving.
    /// </summary>
    sealed class LibraryResolver(AssemblyDefinition library) :
        IAssemblyResolver
    {
        public AssemblyDefinition? Resolve(AssemblyNameReference name)
        {
            if (name.Name == library.Name.Name)
            {
                return library;
            }

            return null;
        }

        public AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters parameters) =>
            Resolve(name);

        public void Dispose()
        {
        }
    }
}
