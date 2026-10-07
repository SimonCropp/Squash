# Diagnostic codes

Every message Squash emits carries a code. The linker's own findings keep the `ILxxxx` codes
[the linker documents](https://learn.microsoft.com/dotnet/core/deploying/trimming/trim-warnings/il2026),
and any of them can be silenced per project:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);IL2026</NoWarn>
</PropertyGroup>
```

`NoWarn` and `WarningsNotAsErrors` hold for a linker warning under `TreatWarningsAsErrors` too: Squash
passes the `ILxxxx` codes in them to the linker. See
[Linker warnings](/readme.md#linker-warnings).

A linker warning marks code the linker could not follow, which is also the code most likely to break
when something it reaches by reflection is trimmed. Silencing one is a statement that the members
involved are kept some other way.


## Squash001

**dotnet host not found.** Error.

The linker is a .NET application, whatever is hosting MSBuild. Squash runs it with the host in the
`DOTNET_HOST_PATH` environment variable, which `dotnet build` sets, and otherwise with the `dotnet`
beside the SDK that is building the project. Neither exists.

Fix: build with the .NET SDK, or set `DOTNET_HOST_PATH` to a `dotnet` executable.


## Squash002

**No .NET runtime for the linker.** Error.

The bundled linker targets one major version of .NET and runs on that version or any later one. The
`dotnet` host that was found has no such runtime. The message carries what the host reported.

Fix: build with a .NET SDK at least as new as the linker (the Squash release notes name it), or
install that runtime beside the SDK in use.


## Squash003

**Linker failed.** Error.

The linker exited with an error and reported no diagnostic of its own. The message carries its exit
code and the last lines it wrote. Its arguments are kept in `obj/.../Squash/squash.rsp`, so the same
run can be repeated by hand:

```
dotnet exec <package>/tools/illink/illink.dll @squash.rsp
```

The assembly the compiler produced has been moved to `obj/.../Squash/in/`. The next build compiles
again, so a failed trim never leaves an untrimmed assembly where a trimmed one is expected.


## Squash004

**Nothing reachable from the public surface.** Error.

The linker kept nothing and wrote no assembly. Squash roots the public and protected surface, and
this assembly has none that anything can reach.

The usual cause is an assembly that holds only internal types and shares them through
`InternalsVisibleTo`, which Squash ignores by default. Fix, for that project:

```xml
<PropertyGroup>
  <SquashInternalsVisibleTo>Honor</SquashInternalsVisibleTo>
</PropertyGroup>
```

Otherwise there is nothing for Squash to do there: set `SquashEnabled` to `false`.


## Squash005

**Symbols could not be rewritten.** Error.

The assembly has a pdb beside it, and the linker could not read it, so the trimmed assembly would
have shipped with symbols that no longer match. Portable and embedded pdbs are read on every
platform. Windows pdbs (`DebugType` of `full` or `pdbonly`) are read only on Windows.

Fix: set `DebugType` to `portable` or `embedded`.


## Squash006

**Assembly could not be signed again.** Error.

The compiler strong-named the assembly, and the linker's rewrite leaves the signature empty, so
Squash signs it again with the same key. That needs the key pair as a `.snk` file. The message says
which of these applies:

 * The key file was not found.
 * The key is held in a key container, which is also where a `.pfx` key ends up.
 * The key file holds only a public key.
 * The key file is not the one the assembly was signed with, which includes an assembly that uses
   `AssemblySignatureKeyAttribute`.

Public-signed and delay-signed assemblies need no key and are not affected.


## Squash007

**Trimming failed.** Error.

An unexpected failure, such as an I/O error moving the assembly. The message carries the exception.
The build fails because it asked for a trimmed assembly and did not get one.


## Squash008

**InternalsVisibleTo ignored.** Message.

The assembly declares friend assemblies, and by default Squash roots only the public surface. Internal
types and members that nothing in the assembly itself reaches have been removed, including any that
only a friend uses. The message names the friends.

The attributes are still in the trimmed assembly, so a friend can use every internal that is left.
A project that references this one is compiled against the trimmed assembly, so a friend that uses
a removed internal fails to compile instead of failing at run time.

To keep a specific member, name it in a [root descriptor](/readme.md#keeping-a-member). To keep
every internal:

```xml
<PropertyGroup>
  <SquashInternalsVisibleTo>Honor</SquashInternalsVisibleTo>
</PropertyGroup>
```


## Squash009

**Invalid input.** Error.

`SquashTask` was given something it cannot work with: an assembly that does not exist, an assembly
name the linker's command line cannot carry, or a linker directory without the linker. The targets
in the package never do this; it means the task was called directly with wrong values.
