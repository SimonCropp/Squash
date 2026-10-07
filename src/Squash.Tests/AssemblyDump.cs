/// <summary>
/// A text listing of what an assembly contains, ordered by name rather than by how the metadata
/// happens to be laid out. The snapshots record this, so a change in what the linker keeps shows up
/// as lines added or removed and nothing else.
/// </summary>
public static class AssemblyDump
{
    // Emitted by the compiler on almost every member, and never what a snapshot is about.
    static HashSet<string> noise =
    [
        "CompilerGenerated",
        "DebuggerHidden",
        "IsReadOnly",
        "Nullable",
        "NullableContext",
        "RefSafetyRules"
    ];

    /// <summary>
    /// Everything: header facts, every type, every field and method with the size of its IL.
    /// </summary>
    public static string Full(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var builder = new StringBuilder();

        builder.Append("flags: ").Append(pe.PEHeaders.CorHeader!.Flags).Append('\n');
        builder.Append("references: ").Append(Join(reader.AssemblyReferences.Select(_ => reader.GetString(reader.GetAssemblyReference(_).Name)))).Append('\n');
        builder.Append("resources: ").Append(Join(reader.ManifestResources.Select(_ => reader.GetString(reader.GetManifestResource(_).Name)))).Append('\n');
        builder.Append("debug: ").Append(Join(pe.ReadDebugDirectory().Select(_ => _.Type.ToString()))).Append('\n');
        builder.Append("attributes:\n");
        foreach (var attribute in AssemblyAttributes(reader))
        {
            builder.Append("  ").Append(attribute).Append('\n');
        }

        builder.Append('\n');
        Types(pe, reader, builder, visibleOnly: false);
        return builder.ToString();
    }

    /// <summary>
    /// Only what another assembly can see without InternalsVisibleTo: the part trimming must never
    /// change.
    /// </summary>
    public static string Surface(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var builder = new StringBuilder();
        Types(pe, pe.GetMetadataReader(), builder, visibleOnly: true);
        return builder.ToString();
    }

    /// <summary>
    /// The size of every method body, by a name that is the same before and after trimming.
    /// </summary>
    public static Dictionary<string, int> BodySizes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var sizes = new Dictionary<string, int>();
        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = TypeName(reader, handle);
            foreach (var method in type.GetMethods().Select(reader.GetMethodDefinition))
            {
                sizes[$"{name}::{Signature(reader, method)}"] = BodySize(pe, method);
            }
        }

        return sizes;
    }

    // The kinds of custom debug information a portable pdb carries, by the GUIDs the format assigns.
    static Dictionary<Guid, string> symbolKinds = new()
    {
        [new("cc110556-a091-4d38-9fec-25ab9a351a6a")] = "SourceLink",
        [new("0e8a571b-6926-466e-b4ad-8ab04611f5fe")] = "EmbeddedSource",
        [new("b5feec05-8cd0-4a83-96da-466284bb4bd8")] = "CompilationOptions",
        [new("7e4d4708-096e-4c5c-aeda-cb10ba6a740d")] = "CompilationMetadataReferences",
        [new("932e74bc-dba9-4478-8d46-0f32a7bab3d3")] = "TypeDefinitionDocuments",
        [new("54fd2ac5-e925-401a-9c2a-f94f171072f8")] = "AsyncMethodSteppingInformation",
        [new("6da9a61e-f8c7-4874-be62-68bc5630df71")] = "StateMachineHoistedLocalScopes"
    };

    /// <summary>
    /// What an assembly's symbols carry besides the mapping from IL to source lines: each kind of
    /// custom debug information, what it is attached to, and how many there are. Reads an embedded
    /// pdb, or the one beside the assembly.
    /// </summary>
    public static string Symbols(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var embedded = pe.ReadDebugDirectory()
            .Where(_ => _.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            .ToList();

        MetadataReaderProvider provider;
        if (embedded.Count == 1)
        {
            provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded[0]);
        }
        else
        {
            provider = MetadataReaderProvider.FromPortablePdbImage([.. File.ReadAllBytes(Path.ChangeExtension(path, ".pdb"))]);
        }

        using (provider)
        {
            var reader = provider.GetMetadataReader();
            var lines = reader.CustomDebugInformation
                .Select(reader.GetCustomDebugInformation)
                .Select(_ => $"{SymbolKind(reader.GetGuid(_.Kind))} on {_.Parent.Kind}")
                .GroupBy(_ => _)
                .OrderBy(_ => _.Key, StringComparer.Ordinal)
                .Select(_ => $"{_.Key}: {_.Count()}");
            return string.Join("\n", lines);
        }
    }

    static string SymbolKind(Guid kind)
    {
        if (symbolKinds.TryGetValue(kind, out var name))
        {
            return name;
        }

        return kind.ToString();
    }

    public static List<string> AssemblyAttributes(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return AssemblyAttributes(pe.GetMetadataReader());
    }

    static List<string> AssemblyAttributes(MetadataReader reader) =>
        reader.GetAssemblyDefinition()
            .GetCustomAttributes()
            .Select(_ => AssemblyAttribute(reader, reader.GetCustomAttribute(_)))
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();

    static string AssemblyAttribute(MetadataReader reader, CustomAttribute attribute)
    {
        var name = AttributeName(reader, attribute);

        // The one assembly attribute whose argument matters here: which friends survive.
        if (name != "InternalsVisibleTo")
        {
            return name;
        }

        var blob = reader.GetBlobReader(attribute.Value);
        blob.ReadUInt16();
        return $"{name}({blob.ReadSerializedString()})";
    }

    static void Types(PEReader pe, MetadataReader reader, StringBuilder builder, bool visibleOnly)
    {
        var types = reader.TypeDefinitions
            .Where(_ => !visibleOnly || IsVisible(reader, _))
            .OrderBy(_ => TypeName(reader, _), StringComparer.Ordinal);
        foreach (var handle in types)
        {
            var type = reader.GetTypeDefinition(handle);
            builder.Append(Access(type.Attributes)).Append(' ').Append(TypeName(reader, handle));
            if (!visibleOnly)
            {
                builder.Append(Attributes(reader, type.GetCustomAttributes()));
            }

            builder.Append('\n');

            var members = new List<string>();
            foreach (var field in type.GetFields().Select(reader.GetFieldDefinition))
            {
                if (visibleOnly &&
                    !IsVisible(field.Attributes))
                {
                    continue;
                }

                var line = $"  {Access(field.Attributes)} field {reader.GetString(field.Name)} : {field.DecodeSignature(Names.Instance, null)}";
                if (!visibleOnly)
                {
                    line += Attributes(reader, field.GetCustomAttributes());
                }

                members.Add(line);
            }

            foreach (var method in type.GetMethods().Select(reader.GetMethodDefinition))
            {
                if (visibleOnly &&
                    !IsVisible(method.Attributes))
                {
                    continue;
                }

                var line = $"  {Access(method.Attributes)} method {Signature(reader, method)}";
                if (!visibleOnly)
                {
                    line += $" il:{BodySize(pe, method)}{Attributes(reader, method.GetCustomAttributes())}";
                }

                members.Add(line);
            }

            foreach (var member in members.OrderBy(_ => _, StringComparer.Ordinal))
            {
                builder.Append(member).Append('\n');
            }
        }
    }

    static string Signature(MetadataReader reader, MethodDefinition method)
    {
        var signature = method.DecodeSignature(Names.Instance, null);
        return $"{reader.GetString(method.Name)}({string.Join(", ", signature.ParameterTypes)}) : {signature.ReturnType}";
    }

    static int BodySize(PEReader pe, MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
        {
            return 0;
        }

        return pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader().Length;
    }

    static string Attributes(MetadataReader reader, CustomAttributeHandleCollection handles)
    {
        var names = handles
            .Select(_ => AttributeName(reader, reader.GetCustomAttribute(_)))
            .Where(_ => !noise.Contains(_))
            .OrderBy(_ => _, StringComparer.Ordinal)
            .ToList();
        if (names.Count == 0)
        {
            return "";
        }

        return $" [{string.Join(", ", names)}]";
    }

    static string AttributeName(MetadataReader reader, CustomAttribute attribute)
    {
        var name = AttributeTypeName(reader, attribute);
        const string suffix = "Attribute";
        if (name.EndsWith(suffix, StringComparison.Ordinal))
        {
            return name[..^suffix.Length];
        }

        return name;
    }

    static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        var constructor = attribute.Constructor;
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            var declaring = reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType();
            return reader.GetString(reader.GetTypeDefinition(declaring).Name);
        }

        var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
        if (parent.Kind == HandleKind.TypeReference)
        {
            return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)parent).Name);
        }

        return "?";
    }

    static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
        {
            return $"{TypeName(reader, declaring)}/{name}";
        }

        var @namespace = reader.GetString(type.Namespace);
        if (@namespace.Length == 0)
        {
            return name;
        }

        return $"{@namespace}.{name}";
    }

    static bool IsVisible(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        var visible = (type.Attributes & TypeAttributes.VisibilityMask) is
            TypeAttributes.Public or
            TypeAttributes.NestedPublic or
            TypeAttributes.NestedFamily or
            TypeAttributes.NestedFamORAssem;
        if (!visible)
        {
            return false;
        }

        var declaring = type.GetDeclaringType();
        return declaring.IsNil ||
               IsVisible(reader, declaring);
    }

    static bool IsVisible(FieldAttributes attributes) =>
        (attributes & FieldAttributes.FieldAccessMask) is
        FieldAttributes.Public or
        FieldAttributes.Family or
        FieldAttributes.FamORAssem;

    static bool IsVisible(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) is
        MethodAttributes.Public or
        MethodAttributes.Family or
        MethodAttributes.FamORAssem;

    static string Access(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
            TypeAttributes.NestedFamily => "protected",
            TypeAttributes.NestedFamORAssem => "protected internal",
            TypeAttributes.NestedFamANDAssem => "private protected",
            TypeAttributes.NestedPrivate => "private",
            _ => "internal"
        };

    static string Access(FieldAttributes attributes) =>
        Access((MethodAttributes)(attributes & FieldAttributes.FieldAccessMask));

    // Field and method access share their values.
    static string Access(MethodAttributes attributes) =>
        (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family => "protected",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.FamANDAssem => "private protected",
            MethodAttributes.Assembly => "internal",
            _ => "private"
        };

    static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.OrderBy(_ => _, StringComparer.Ordinal));

    /// <summary>
    /// Type names for signatures: enough to tell overloads apart and read at a glance.
    /// </summary>
    sealed class Names :
        ISignatureTypeProvider<string, object?>
    {
        public static readonly Names Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            typeCode switch
            {
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Byte => "byte",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.Double => "double",
                PrimitiveTypeCode.Int16 => "short",
                PrimitiveTypeCode.Int32 => "int",
                PrimitiveTypeCode.Int64 => "long",
                PrimitiveTypeCode.Object => "object",
                PrimitiveTypeCode.SByte => "sbyte",
                PrimitiveTypeCode.Single => "float",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.UInt16 => "ushort",
                PrimitiveTypeCode.UInt32 => "uint",
                PrimitiveTypeCode.UInt64 => "ulong",
                PrimitiveTypeCode.Void => "void",
                _ => typeCode.ToString()
            };

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetSZArrayType(string elementType) =>
            $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            $"{elementType}[{new string(',', shape.Rank - 1)}]";

        public string GetByReferenceType(string elementType) =>
            $"ref {elementType}";

        public string GetPointerType(string elementType) =>
            $"{elementType}*";

        public string GetPinnedType(string elementType) =>
            elementType;

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
            unmodifiedType;

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            $"{genericType}<{string.Join(", ", typeArguments)}>";

        public string GetGenericMethodParameter(object? genericContext, int index) =>
            $"!!{index}";

        public string GetGenericTypeParameter(object? genericContext, int index) =>
            $"!{index}";

        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            "fnptr";
    }
}
