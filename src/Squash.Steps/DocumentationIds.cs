using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Methods = System.Reflection.MethodAttributes;

/// <summary>
/// The name the compiler gives each type and member of an assembly in its documentation file: the
/// value of a member element's name attribute. Read from the file with the framework's own metadata
/// reader, and nothing of the linker's, so the same code names what went into the linker and what
/// came out.
/// </summary>
static class DocumentationIds
{
    public static HashSet<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new TypeNames(reader);
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = names.Definition(handle);
            ids.Add($"T:{name}");

            foreach (var field in type.GetFields())
            {
                ids.Add($"F:{name}.{reader.GetString(reader.GetFieldDefinition(field).Name)}");
            }

            foreach (var method in type.GetMethods())
            {
                ids.Add($"M:{name}.{Method(reader, names, method)}");
            }

            foreach (var property in type.GetProperties())
            {
                var definition = reader.GetPropertyDefinition(property);

                // An indexer is told from its overloads by its parameters, as a method is.
                var parameters = definition.DecodeSignature(names, null).ParameterTypes;
                ids.Add($"P:{name}.{Escape(reader.GetString(definition.Name))}{Parameters(parameters, false)}");
            }

            foreach (var @event in type.GetEvents())
            {
                ids.Add($"E:{name}.{Escape(reader.GetString(reader.GetEventDefinition(@event).Name))}");
            }
        }

        return ids;
    }

    static string Method(MetadataReader reader, TypeNames names, MethodDefinitionHandle handle)
    {
        var method = reader.GetMethodDefinition(handle);
        var name = reader.GetString(method.Name);
        var signature = method.DecodeSignature(names, null);
        var builder = new StringBuilder(Escape(name));
        if (signature.GenericParameterCount > 0)
        {
            builder.Append("``");
            builder.Append(signature.GenericParameterCount);
        }

        builder.Append(Parameters(signature.ParameterTypes, signature.Header.CallingConvention == SignatureCallingConvention.VarArgs));

        // Conversions overload on what they return, so that is part of the name.
        if (IsConversion(method.Attributes, name))
        {
            builder.Append('~');
            builder.Append(signature.ReturnType);
        }

        return builder.ToString();
    }

    static bool IsConversion(Methods attributes, string name)
    {
        const Methods conversion = Methods.SpecialName | Methods.Static;
        return (attributes & conversion) == conversion &&
               name is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";
    }

    static string Parameters(ImmutableArray<string> parameters, bool variable)
    {
        if (parameters.Length == 0)
        {
            // Nothing at all for no parameters, except where the list is open ended.
            if (variable)
            {
                return "()";
            }

            return "";
        }

        var list = string.Join(",", parameters);
        if (variable)
        {
            return $"({list},)";
        }

        return $"({list})";
    }

    /// <summary>
    /// A member that implements an interface member explicitly carries the interface in its name,
    /// "System.IDisposable.Dispose", after any extern alias. A dot there would read as a level of
    /// nesting, and angle brackets cannot go in an attribute.
    /// </summary>
    static string Escape(string name)
    {
        var alias = name.IndexOf("::", StringComparison.Ordinal);
        if (alias >= 0)
        {
            name = name[(alias + 2)..];
        }

        return name
            .Replace('.', '#')
            .Replace('<', '{')
            .Replace('>', '}');
    }

    /// <summary>
    /// Types as a signature names them. Nothing here depends on where a type parameter was declared,
    /// only on its position, so there is no generic context.
    /// </summary>
    sealed class TypeNames(MetadataReader reader) :
        ISignatureTypeProvider<string, object?>
    {
        Dictionary<TypeDefinitionHandle, string> definitions = [];

        /// <summary>
        /// The namespace, then each enclosing type, then the type, all joined by dots.
        /// </summary>
        public string Definition(TypeDefinitionHandle handle)
        {
            if (definitions.TryGetValue(handle, out var cached))
            {
                return cached;
            }

            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (declaring.IsNil)
            {
                name = Qualify(reader.GetString(type.Namespace), name);
            }
            else
            {
                name = $"{Definition(declaring)}.{name}";
            }

            definitions[handle] = name;
            return name;
        }

        string Reference(TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return $"{Reference((TypeReferenceHandle)type.ResolutionScope)}.{name}";
            }

            return Qualify(reader.GetString(type.Namespace), name);
        }

        static string Qualify(string @namespace, string name)
        {
            if (@namespace.Length == 0)
            {
                return name;
            }

            return $"{@namespace}.{name}";
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            Definition(handle);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            Reference(handle);

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            $"System.{typeCode}";

        public string GetSZArrayType(string elementType) =>
            $"{elementType}[]";

        public string GetArrayType(string elementType, ArrayShape shape) =>
            $"{elementType}[{string.Join(",", Enumerable.Repeat("0:", shape.Rank))}]";

        public string GetByReferenceType(string elementType) =>
            $"{elementType}@";

        public string GetPointerType(string elementType) =>
            $"{elementType}*";

        public string GetPinnedType(string elementType) =>
            elementType;

        // readonly, volatile and the like are not part of a name.
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) =>
            unmodifiedType;

        public string GetGenericTypeParameter(object? genericContext, int index) =>
            $"`{index}";

        public string GetGenericMethodParameter(object? genericContext, int index) =>
            $"``{index}";

        // The compiler writes nothing for a function pointer.
        public string GetFunctionPointerType(MethodSignature<string> signature) =>
            "";

        /// <summary>
        /// "Outer`1.Inner`2" with three arguments gives "Outer{a}.Inner{b,c}". The arguments of every
        /// level of nesting arrive as one list, and each level's name says how many are its own.
        /// </summary>
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            var builder = new StringBuilder();
            var taken = 0;
            foreach (var level in genericType.Split('.'))
            {
                if (builder.Length > 0)
                {
                    builder.Append('.');
                }

                var tick = level.LastIndexOf('`');
                if (tick < 0 ||
                    !int.TryParse(level.AsSpan(tick + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var arity))
                {
                    builder.Append(level);
                    continue;
                }

                builder.Append(level, 0, tick);
                var own = typeArguments.Skip(taken).Take(arity).ToList();
                taken += own.Count;
                Arguments(builder, own);
            }

            // A name that does not say how many it takes.
            Arguments(builder, typeArguments.Skip(taken).ToList());
            return builder.ToString();
        }

        static void Arguments(StringBuilder builder, List<string> arguments)
        {
            if (arguments.Count == 0)
            {
                return;
            }

            builder.Append('{');
            builder.AppendJoin(',', arguments);
            builder.Append('}');
        }
    }
}
