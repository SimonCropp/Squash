using ILVerify;

/// <summary>
/// Ways of asking whether a rewritten assembly still works, none of which depend on knowing what
/// the linker did to it.
/// </summary>
public static class Checks
{
    /// <summary>
    /// What ILVerify objects to. The assembly is resolved against the references it was compiled
    /// with, as the linker resolved it.
    /// </summary>
    public static List<string> IlErrors(string assembly, IEnumerable<string> references)
    {
        using var resolver = new Resolver(assembly, references);
        var verifier = new ILVerify.Verifier(resolver);
        verifier.SetSystemModuleName(new(resolver.SystemModule()));
        return verifier
            .Verify(resolver.Open(assembly))
            .Select(_ => $"{_.Code}: {string.Format(_.Message, _.Args ?? [])}")
            .ToList();
    }

    /// <summary>
    /// Asks the runtime to compile every method. A reference to a member that is no longer there
    /// fails here, where nothing but loading the type would have noticed.
    /// </summary>
    public static List<string> JitFailures(string assembly, string dependencies)
    {
        var failures = new List<string>();
        var context = new AssemblyLoadContext("jit", isCollectible: true);
        context.Resolving += (_, name) =>
        {
            var candidate = Path.Combine(dependencies, name.Name + ".dll");
            if (File.Exists(candidate))
            {
                return context.LoadFromStream(new MemoryStream(File.ReadAllBytes(candidate)));
            }

            return null;
        };

        try
        {
            // From memory, so the file is not held open.
            var loaded = context.LoadFromStream(new MemoryStream(File.ReadAllBytes(assembly)));
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (var type in loaded.GetTypes())
            {
                if (type.ContainsGenericParameters)
                {
                    continue;
                }

                var methods = type
                    .GetMethods(all)
                    .Cast<MethodBase>()
                    .Concat(type.GetConstructors(all));
                foreach (var method in methods)
                {
                    if (method.IsAbstract ||
                        method.ContainsGenericParameters)
                    {
                        continue;
                    }

                    try
                    {
                        RuntimeHelpers.PrepareMethod(method.MethodHandle);
                    }
                    catch (Exception exception)
                    {
                        failures.Add($"{type.FullName}.{method.Name}: {exception.GetType().Name} {exception.Message}");
                    }
                }
            }
        }
        catch (ReflectionTypeLoadException exception)
        {
            failures.AddRange(exception.LoaderExceptions.Select(_ => _?.Message ?? ""));
        }
        finally
        {
            context.Unload();
        }

        return failures;
    }

    public sealed record SymbolFacts(Guid AssemblyId, Guid SymbolsId, int Methods, int MethodsWithSymbols, bool Embedded);

    /// <summary>
    /// The identity an assembly records for its symbols against the identity the symbols carry, and
    /// whether the symbols describe the same methods. Reads an embedded pdb, or the one beside the
    /// assembly.
    /// </summary>
    public static SymbolFacts Symbols(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var entries = pe.ReadDebugDirectory();
        var codeView = pe.ReadCodeViewDebugDirectoryData(entries.Single(_ => _.Type == DebugDirectoryEntryType.CodeView));
        var embedded = entries
            .Where(_ => _.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
            .ToList();

        MetadataReaderProvider provider;
        if (embedded.Count == 1)
        {
            provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded[0]);
        }
        else
        {
            provider = MetadataReaderProvider.FromPortablePdbImage([.. File.ReadAllBytes(Path.ChangeExtension(assembly, ".pdb"))]);
        }

        using (provider)
        {
            var symbols = provider.GetMetadataReader();
            return new(
                codeView.Guid,
                new BlobContentId(symbols.DebugMetadataHeader!.Id).Guid,
                pe.GetMetadataReader().MethodDefinitions.Count,
                symbols.MethodDebugInformation.Count,
                embedded.Count == 1);
        }
    }

    public static bool HasEntryPoint(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        return pe.PEHeaders.CorHeader!.EntryPointTokenOrRelativeVirtualAddress != 0;
    }

    sealed class Resolver :
        IResolver,
        IDisposable
    {
        readonly Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, PEReader> readers = new(StringComparer.OrdinalIgnoreCase);

        public Resolver(string assembly, IEnumerable<string> references)
        {
            paths[Path.GetFileNameWithoutExtension(assembly)] = assembly;
            foreach (var reference in references)
            {
                paths.TryAdd(Path.GetFileNameWithoutExtension(reference), reference);
            }
        }

        public PEReader Open(string path)
        {
            if (!readers.TryGetValue(path, out var reader))
            {
                reader = new(File.OpenRead(path));
                readers[path] = reader;
            }

            return reader;
        }

        /// <summary>
        /// The reference that defines System.Object: netstandard, mscorlib or System.Runtime,
        /// depending on what the assembly was compiled against.
        /// </summary>
        public string SystemModule()
        {
            foreach (var name in new[] { "System.Runtime", "netstandard", "mscorlib" })
            {
                if (!paths.TryGetValue(name, out var path))
                {
                    continue;
                }

                var reader = Open(path).GetMetadataReader();
                var defines = reader.TypeDefinitions
                    .Select(reader.GetTypeDefinition)
                    .Any(_ => reader.GetString(_.Name) == "Object" && reader.GetString(_.Namespace) == "System");
                if (defines)
                {
                    return name;
                }
            }

            throw new("No reference defines System.Object.");
        }

        public PEReader ResolveAssembly(AssemblyNameInfo assemblyName)
        {
            if (paths.TryGetValue(assemblyName.Name, out var path))
            {
                return Open(path);
            }

            return null!;
        }

        public PEReader ResolveModule(AssemblyNameInfo referencingAssembly, string fileName) =>
            null!;

        public void Dispose()
        {
            foreach (var reader in readers.Values)
            {
                reader.Dispose();
            }
        }
    }
}
