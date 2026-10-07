/// <summary>
/// What friend assemblies use of one assembly that its visible surface does not already keep, as a
/// root descriptor. Read from the friends' metadata, so it holds what they refer to, and a little
/// more for what they can use without referring to it.
/// </summary>
sealed class FriendRoots(AssemblyDefinition library)
{
    readonly SortedDictionary<string, Entry> types = new(StringComparer.Ordinal);

    sealed class Entry
    {
        public string? Preserve;
        public SortedSet<string> Fields { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Methods { get; } = new(StringComparer.Ordinal);
    }

    public void Add(ModuleDefinition friend)
    {
        foreach (var reference in friend.GetTypeReferences())
        {
            if (Resolve(reference) is { } type)
            {
                Add(type);
            }
        }

        foreach (var reference in friend.GetMemberReferences())
        {
            if (!InLibrary(reference.DeclaringType))
            {
                continue;
            }

            if (reference is MethodReference method &&
                method.Resolve() is { } methodDefinition)
            {
                Add(methodDefinition);
            }

            if (reference is FieldReference field &&
                field.Resolve() is { } fieldDefinition)
            {
                Add(fieldDefinition);
            }
        }

        foreach (var type in friend.GetTypes())
        {
            AddOverridable(type.BaseType);
            foreach (var implemented in type.Interfaces)
            {
                AddOverridable(implemented.InterfaceType);
            }
        }

        AddCompilerSupport();
    }

    /// <summary>
    /// The compiler insists on these for ^ and .., and then compiles most uses of them down to
    /// arithmetic, so a friend can need them without referring to them. Only where the root assembly
    /// carries copies of its own, as it does for a framework that lacks them.
    /// </summary>
    void AddCompilerSupport()
    {
        foreach (var name in new[] { "System.Index", "System.Range" })
        {
            var type = library.MainModule.GetType(name);
            if (type != null &&
                !IsVisible(type))
            {
                For(type).Preserve = "all";
            }
        }
    }

    void Add(TypeDefinition type)
    {
        if (!IsVisible(type))
        {
            var entry = For(type);
            if (IsAttribute(type))
            {
                // Where it is applied, its properties are named in a blob and not referred to.
                entry.Preserve = "all";
            }
            else if (type.IsEnum)
            {
                entry.Preserve ??= "fields";
            }
        }

        // A constant is copied into the friend when it compiles and leaves no reference behind, so
        // every constant of a type the friend does refer to is kept.
        foreach (var field in type.Fields)
        {
            if (field.IsLiteral &&
                !field.IsPrivate &&
                !IsVisible(field))
            {
                For(type).Fields.Add(Signature(field));
            }
        }
    }

    void Add(MethodDefinition method)
    {
        if (IsVisible(method))
        {
            return;
        }

        For(method.DeclaringType).Methods.Add(Signature(method));
        AddExtensionDeclaration(method);
    }

    /// <summary>
    /// A member of an extension block is compiled twice: as an implementation, which is what a
    /// caller's code refers to, and as a declaration in a nested grouping type, which is all the
    /// compiler binds a call against. A friend needs both and refers only to the first. Kept in
    /// pairs, so that a declaration is never there without the implementation it leads to.
    /// </summary>
    void AddExtensionDeclaration(MethodDefinition implementation)
    {
        if (!implementation.IsStatic)
        {
            return;
        }

        foreach (var grouping in implementation.DeclaringType.NestedTypes)
        {
            if (!grouping.Name.StartsWith("<G>$", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var declaration in grouping.Methods)
            {
                if (!Declares(declaration, implementation))
                {
                    continue;
                }

                For(grouping).Methods.Add(Signature(declaration));

                // The marker types, which say what the block extends.
                foreach (var marker in grouping.NestedTypes)
                {
                    For(marker).Preserve = "all";
                }
            }
        }
    }

    static bool Declares(MethodDefinition declaration, MethodDefinition implementation)
    {
        if (declaration.Name != implementation.Name)
        {
            return false;
        }

        // An instance member's implementation takes what it extends as its first parameter.
        var receiver = 1;
        if (declaration.IsStatic)
        {
            receiver = 0;
        }

        return implementation.Parameters
            .Skip(receiver)
            .Select(_ => _.ParameterType.FullName)
            .SequenceEqual(declaration.Parameters.Select(_ => _.ParameterType.FullName));
    }

    void Add(FieldDefinition field)
    {
        if (!IsVisible(field))
        {
            For(field.DeclaringType).Fields.Add(Signature(field));
        }
    }

    /// <summary>
    /// A friend that overrides or implements a member needs it to be there, and refers to it only
    /// where it calls it.
    /// </summary>
    void AddOverridable(TypeReference? reference)
    {
        if (reference == null)
        {
            return;
        }

        // Up through the bases declared in the root assembly. One declared elsewhere is a reference,
        // not a definition, and ends the walk.
        for (var type = Resolve(reference); type != null; type = type.BaseType?.GetElementType() as TypeDefinition)
        {
            foreach (var method in type.Methods)
            {
                if (method.IsVirtual &&
                    !method.IsPrivate)
                {
                    Add(method);
                }
            }
        }
    }

    TypeDefinition? Resolve(TypeReference reference)
    {
        if (InLibrary(reference))
        {
            return reference.Resolve();
        }

        return null;
    }

    bool InLibrary(TypeReference reference) =>
        reference.GetElementType().Scope is AssemblyNameReference scope &&
        scope.Name == library.Name.Name;

    Entry For(TypeDefinition type)
    {
        if (!types.TryGetValue(type.FullName, out var entry))
        {
            entry = new();
            types.Add(type.FullName, entry);
        }

        return entry;
    }

    // What library mode roots without being asked.
    static bool IsVisible(TypeDefinition type)
    {
        if (!type.IsNested)
        {
            return type.IsPublic;
        }

        if (type.IsNestedPublic ||
            type.IsNestedFamily ||
            type.IsNestedFamilyOrAssembly)
        {
            return IsVisible(type.DeclaringType);
        }

        return false;
    }

    static bool IsVisible(MethodDefinition method) =>
        IsVisible(method.DeclaringType) &&
        (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

    static bool IsVisible(FieldDefinition field) =>
        IsVisible(field.DeclaringType) &&
        (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly);

    static bool IsAttribute(TypeDefinition type)
    {
        for (var current = type; current != null; current = current.BaseType?.GetElementType() as TypeDefinition)
        {
            if (current.BaseType?.FullName == "System.Attribute")
            {
                return true;
            }
        }

        return false;
    }

    // Both as the linker builds them when it looks a descriptor's member up.
    static string Signature(MethodDefinition method) =>
        $"{method.ReturnType.FullName} {method.Name}({string.Join(",", method.Parameters.Select(_ => _.ParameterType.FullName))})";

    static string Signature(FieldDefinition field) =>
        $"{field.FieldType.FullName} {field.Name}";

    public string ToXml()
    {
        var builder = new StringBuilder();
        builder.Append("<linker>\n");
        builder.Append("  <!-- Written by Squash: what the assemblies named in InternalsVisibleTo use of this one.\n");
        builder.Append("       Build with SquashFriendRoots set to Update to write it again. -->\n");
        if (types.Count > 0)
        {
            builder.Append($"  <assembly fullname=\"{Escape(library.Name.Name)}\">\n");
            foreach (var pair in types)
            {
                Append(builder, pair.Key, pair.Value);
            }

            builder.Append("  </assembly>\n");
        }

        builder.Append("</linker>\n");
        return builder.ToString();
    }

    static void Append(StringBuilder builder, string name, Entry entry)
    {
        var preserve = entry.Preserve;

        // Already said by the preserve.
        var fields = entry.Fields;
        var methods = entry.Methods;
        if (preserve is "all" or "fields")
        {
            fields = [];
        }

        if (preserve == "all")
        {
            methods = [];
        }

        var members = fields.Count + methods.Count;
        if (preserve == null &&
            members == 0)
        {
            // A type named with no members and no preserve keeps all of its members.
            preserve = "nothing";
        }

        builder.Append($"    <type fullname=\"{Escape(name)}\"");
        if (preserve != null)
        {
            builder.Append($" preserve=\"{preserve}\"");
        }

        if (members == 0)
        {
            builder.Append(" />\n");
            return;
        }

        builder.Append(">\n");
        foreach (var field in fields)
        {
            builder.Append($"      <field signature=\"{Escape(field)}\" />\n");
        }

        foreach (var method in methods)
        {
            builder.Append($"      <method signature=\"{Escape(method)}\" />\n");
        }

        builder.Append("    </type>\n");
    }

    static string Escape(string value) =>
        value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
