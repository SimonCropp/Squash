static class RootAssembly
{
    /// <summary>
    /// Loads the root assembly. The steps that run before the linker's own root step need this: until
    /// that step runs nothing has been loaded.
    /// </summary>
    public static AssemblyDefinition? Resolve(LinkContext context)
    {
        var name = CustomData.Get(context, CustomData.RootAssembly);
        if (name == null)
        {
            return null;
        }

        // The version is not part of how the linker finds an assembly, only the name is.
        return context.Resolve(new AssemblyNameReference(name, new()));
    }

    public static AssemblyDefinition? Loaded(LinkContext context)
    {
        var name = CustomData.Get(context, CustomData.RootAssembly);
        if (name == null)
        {
            return null;
        }

        return context.GetLoadedAssembly(name);
    }

    public static bool Contains(LinkContext context, TypeDefinition type) =>
        type.Module.Assembly.Name.Name == CustomData.Get(context, CustomData.RootAssembly);
}
