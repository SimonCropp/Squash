# <img src="/src/icon.png" height="30px"> Squash

[![Build status](https://github.com/SimonCropp/Squash/actions/workflows/build.yml/badge.svg)](https://github.com/SimonCropp/Squash/actions/workflows/build.yml)
[![NuGet Status](https://img.shields.io/nuget/v/Squash.svg)](https://www.nuget.org/packages/Squash/)

Removes unreachable IL from a library at build time. The public surface is untouched; non-public types and members that nothing reaches are trimmed. The trimming is done by the [.NET IL linker](https://github.com/dotnet/runtime/tree/main/src/tools/illink), the tool behind `PublishTrimmed`, run unmodified against the one assembly.

**See [Milestones](../../milestones?state=closed) for release notes.**


## Why

A library carries code that nothing can reach: polyfills and other source-only packages compiled into it, helpers that outlived their callers, the unused half of a shared file. An application can trim that away when it is published. A library cannot, and most applications are never trimmed, so the dead code ships to everyone.

A library that uses four APIs from [Polyfill](https://github.com/SimonCropp/Polyfill) 11.4.3:

| Target framework | Without Squash | With Squash |
|---|--:|--:|
| `netstandard2.0` | 212,992 bytes | 11,776 bytes |
| `net10.0` | 79,872 bytes | 8,704 bytes |

dotnet/runtime [trims its own libraries the same way](https://github.com/dotnet/runtime/blob/f5f66b5f3ceadcd7daca3098b48ba5c8930f01f7/eng/illink.targets#L202) as it builds them, with the same linker in the same mode. Squash packages that for any library.


## Usage

Reference the package:

```xml
<PackageReference Include="Squash" Version="x.y.z" PrivateAssets="all" />
```

A C# library needs nothing more. On an optimized build, straight after the compiler has run, the intermediate assembly in `obj` is replaced by the trimmed one, so what is copied to `bin`, packed, and seen by referencing projects is the trimmed assembly.

By default Squash runs when all of these hold:

 * `Optimize` is `true`, which is a Release build. Debug builds are left alone, so the inner loop and Hot Reload are unaffected.
 * `OutputType` is `Library`.
 * The project is C#, and is not a WPF, Razor or test project.

`SquashEnabled` set to `true` or `false` overrides all of that. An executable that opts in has its entry point kept.

The build machine needs a .NET 10 or later runtime, which the .NET 10 SDK includes. The linker runs on it whatever the project targets, and whether the build is `dotnet build` or `msbuild.exe`.


## What is kept

The linker starts from the visible surface and keeps what it reaches.

Kept:

 * Public types, and their public, `protected` and `protected internal` members.
 * Everything those reach, directly or indirectly, however it is declared.
 * Every interface, and the members that implement or override something declared in another assembly.
 * Serialization constructors and `[OnSerializing]`-style callbacks of types that are kept.
 * Assembly attributes, resources, and the symbols for everything that is left.

Removed, unless something that is kept reaches them:

 * `internal` and `private` types.
 * `internal`, `private` and `private protected` members, of any type.

The linker is asked to remove members, not to rewrite the ones that stay. Squash turns off the optimization that would fold a constant from a dependency into the library, since the application may run a different build of that dependency.

Every build writes `obj/{configuration}/{framework}/Squash/removed.txt`, listing each type, method and field that was removed, and keeps the assembly the compiler produced in the `in` folder beside it.


## Reflection

The linker follows code, and reflection it can read: `typeof`, `Type.GetMethod("Name")` with a constant, members named by `[DynamicallyAccessedMembers]`. It cannot follow a serializer, a mapper or a container finding members at run time. A member reached only that way is removed like any other dead code, without a warning, and the failure shows up at run time: often as a property left unset, not as an exception.

Typical casualties are a private setter or private constructor used only by a serializer, a converter named only by `typeof` in an attribute, and an internal type found by scanning the assembly. On .NET the framework's own attributes are annotated, so the linker follows more; on `netstandard2.0` and .NET Framework nothing is.

Read `removed.txt` after first enabling Squash. Anything in it that is used after all needs one of the following.


### Keeping a member

**In code**, with `DynamicDependency` on something that is kept. The attribute ships in .NET 5 and later; on older frameworks the linker recognises it by name, so a copy from [Polyfill](https://github.com/SimonCropp/Polyfill) works.

```cs
public class Orders
{
    // Every member of OrderDto, for as long as Load is kept.
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(OrderDto))]
    public Order Load(string json) =>
        Map(Deserialize<OrderDto>(json));
}
```

**In a descriptor**, an XML file in the
[linker's format](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/data-formats.md#descriptor-format):

```xml
<linker>
  <assembly fullname="MyLibrary">
    <type fullname="MyLibrary.OrderDto" preserve="all" />
    <type fullname="MyLibrary.Handlers.*" />
  </assembly>
</linker>
```

```xml
<ItemGroup>
  <SquashRootDescriptor Include="roots.xml" />
</ItemGroup>
```

**For every type at once**, with `SquashPreserve`:

```xml
<PropertyGroup>
  <SquashPreserve>DataShape</SquashPreserve>
</PropertyGroup>
```

With `DataShape`, a type that is kept also keeps all of its fields, property accessors and instance constructors, which is what serializers, mappers and ORMs look for. Types and methods that nothing reaches are still removed. It trims less, and it does not help a type that is only ever found by scanning.


## InternalsVisibleTo

The linker on its own keeps every internal type and member of an assembly that has any `InternalsVisibleTo`, which for a library with a test project means almost nothing is removed. Squash ignores the attribute by default: only the public surface is the root, and an internal that only a friend assembly uses is removed.

 * The attributes stay in the trimmed assembly. A friend can use every internal that is left.
 * A project that references the library is compiled against the trimmed assembly, not the compiler's reference assembly. A friend that uses a removed internal therefore fails to compile, with the usual error for a missing member, instead of failing at run time.
 * The build reports which friends were ignored ([Squash008](/docs/DiagnosticCodes.md#squash008)).
 * Tests normally run against a Debug build, which is not trimmed.

To keep what a friend needs, name it in a [descriptor](#keeping-a-member). To restore the linker's behaviour and keep every internal:

```xml
<PropertyGroup>
  <SquashInternalsVisibleTo>Honor</SquashInternalsVisibleTo>
</PropertyGroup>
```

An assembly with no public types at all, shared entirely through `InternalsVisibleTo`, needs `Honor`: without it nothing is reachable ([Squash004](/docs/DiagnosticCodes.md#squash004)).


## Settings

| Property or item | Default | |
|---|---|---|
| `SquashEnabled` | see [Usage](#usage) | `true` or `false` overrides the defaults. |
| `SquashInternalsVisibleTo` | `Ignore` | `Honor` keeps every internal when the assembly has friends. |
| `SquashPreserve` | `None` | `DataShape` keeps the fields, property accessors and constructors of kept types. |
| `SquashTreatWarningsAsErrors` | `$(TreatWarningsAsErrors)` | Whether a linker warning fails the build. |
| `SquashRootDescriptor` | none | Item. Descriptor files naming members to keep. |
| `SquashExtraArgs` | empty | Appended to the linker's arguments, after Squash's own. |

Changing any of them compiles and trims again; nothing needs cleaning.

`SquashExtraArgs` takes any [linker option](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md). Where one contradicts an argument Squash passes, the later one wins. For example `--enable-opt ipconstprop` turns constant folding back on. More examples are in [docs/LinkerArguments.md](/docs/LinkerArguments.md).


## Linker warnings

Where the linker cannot follow a piece of reflection it says so, with the `ILxxxx` codes and source locations it uses when trimming an application. They are build warnings, and `NoWarn`, `WarningsAsErrors`, `WarningsNotAsErrors` and `TreatWarningsAsErrors` apply to them as to any other.

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <!-- Never reported. -->
  <NoWarn>$(NoWarn);IL2026</NoWarn>
  <!-- Reported, as a warning. -->
  <WarningsNotAsErrors>$(WarningsNotAsErrors);IL2070</WarningsNotAsErrors>
</PropertyGroup>
```

With `TreatWarningsAsErrors` it is the linker that turns its warnings into errors, so Squash passes it the `ILxxxx` codes from `NoWarn` and `WarningsNotAsErrors`, as `--nowarn` and `--warnaserror-`. Only codes of that form are passed. Compiler, analyzer and NuGet codes in the same lists are left out, as is a bare number such as `1591`, which means `CS1591` to the compiler and would mean `IL1591` to the linker. `SquashTreatWarningsAsErrors` set to `false` leaves every linker warning a warning.

A warning marks the code most likely to break, so each is worth reading once. Annotating the code ([Prepare .NET libraries for trimming](https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming)) fixes it for applications that trim as well.

Squash's own codes are in [docs/DiagnosticCodes.md](/docs/DiagnosticCodes.md).


## Strong names and symbols

 * A strong-named assembly is signed again after trimming, with the same `.snk` key file the compiler used. Public-signed and delay-signed assemblies need nothing. A key in a key container, which includes a `.pfx`, cannot be used ([Squash006](/docs/DiagnosticCodes.md#squash006)).
 * Portable and embedded pdbs are rewritten to match the trimmed assembly: the line mappings of every method that is left, source link, and the compiler's record of its options and references.
 * The same input gives the same bytes, assembly and pdb.


## The linker

The package carries its own copy of the linker, copied unmodified from the [Microsoft.NET.ILLink.Tasks](https://www.nuget.org/packages/Microsoft.NET.ILLink.Tasks) package, so a given version of Squash trims the same way on every machine, SDK and target framework. The .NET SDK itself contains no linker, and restores that package only for some projects and at a version that varies by target framework.

Squash talks to it through its command line and nothing else, plus four small steps loaded through the linker's `--custom-step` extension point, compiled against the exact linker they ship with. The arguments of the last run are in `obj/{configuration}/{framework}/Squash/squash.rsp`.

Linker patches arrive through Dependabot and are merged once the tests pass. The tests snapshot what survives trimming, on three target frameworks, so a linker that keeps or removes something different shows as a diff. A weekly workflow runs the same tests against the linker's prereleases and daily builds.


## Limits

 * Types found only by scanning an assembly are removed. Root them with a descriptor.
 * A reference whose file name differs from its assembly name cannot be resolved by the linker.
 * Windows pdbs (`DebugType` of `full` or `pdbonly`) can be rewritten only on Windows ([Squash005](/docs/DiagnosticCodes.md#squash005)).
 * Source embedded in a pdb (`EmbedAllSources`, `EmbedUntrackedSources`) is kept for a file that still has code in the trimmed assembly, and dropped for a file that holds only declarations or attributes. Source link is unaffected.
 * Only the project's own assembly is trimmed, not the assemblies it references.
 * F# and Visual Basic projects are not enabled by default, and are untested.


## Icon

[Squash](https://thenounproject.com/icon/squash-5884986/) from [The Noun Project](https://thenounproject.com)
