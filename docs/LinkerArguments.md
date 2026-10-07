# Linker arguments

`SquashExtraArgs` is text added to the linker's arguments, after the ones Squash passes. It takes
any option the linker has. The linker documents them in
[Available Command Line Options](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md),
and lists every one, including those that page leaves out, in the `--help` text in
[Driver.cs](https://github.com/dotnet/runtime/blob/main/src/tools/illink/src/linker/Linker/Driver.cs).

```xml
<PropertyGroup>
  <SquashExtraArgs>--singlewarn --dump-dependencies</SquashExtraArgs>
</PropertyGroup>
```

 * Several options go in the one property, separated by spaces.
 * A file the linker writes lands in `obj/{configuration}/{framework}/Squash/out`. It is replaced
   by the next trim.
 * Where an option contradicts one Squash passes, the later one wins, which is the one here.
 * Changing the property compiles and trims again.
 * The arguments of the last run, these included, are in `squash.rsp` in the `Squash` directory.

The examples below were run against the linker this version of Squash carries.


## Finding out why something was kept

[`--dump-dependencies`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#detailed-dependencies-tracing)
records, for everything the linker kept, what it was reached from.

```xml
<PropertyGroup>
  <SquashExtraArgs>--dump-dependencies</SquashExtraArgs>
</PropertyGroup>
```

The result is `out/linker-dependencies.xml`. `--dependencies-file` gives it another name, in
the same folder:

```xml
<PropertyGroup>
  <SquashExtraArgs>--dump-dependencies --dependencies-file why.xml</SquashExtraArgs>
</PropertyGroup>
```

The opposite question, what was removed, needs no option: every build writes `removed.txt`.


## One warning for the assembly

[`--singlewarn`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#emit-single-warnings-per-assembly)
replaces every trim analysis warning with a single
[IL2104](https://learn.microsoft.com/dotnet/core/deploying/trimming/trim-warnings/il2104) saying
that the assembly produced some.

```xml
<PropertyGroup>
  <SquashExtraArgs>--singlewarn</SquashExtraArgs>
</PropertyGroup>
```

It suits a library that is known not to be annotated for trimming, where the individual warnings
are not going to be acted on.


## Generating suppressions

[`--generate-warning-suppressions`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#generating-warning-suppressions)
writes an `UnconditionalSuppressMessage` attribute for each warning the linker reported, as C# or
as XML in the linker's
[custom attributes format](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/data-formats.md#custom-attributes-annotations-format).

```xml
<PropertyGroup>
  <SquashExtraArgs>--generate-warning-suppressions cs</SquashExtraArgs>
</PropertyGroup>
```

The result is `out/{AssemblyName}.WarningSuppressions.cs`. The warnings are still reported on that
build. Copying the attributes that have been checked into the project suppresses them from then on,
for an application that trims the library as well.


## Warnings from newer linkers

[`--warn`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#control-warning-versions)
hides the warnings introduced after a given version, so that a newer linker reports no more than an
older one did. Squash leaves it at the linker's default of `9999`, which is every warning.

```xml
<PropertyGroup>
  <SquashExtraArgs>--warn 5</SquashExtraArgs>
</PropertyGroup>
```


## Seeing what the linker is doing

`--verbose` logs progress, and the references the linker could not resolve and went on without.

```xml
<PropertyGroup>
  <SquashExtraArgs>--verbose</SquashExtraArgs>
</PropertyGroup>
```

The lines are low importance, so they show with `dotnet build -v:detailed` or in a binary log.


## Optimizations

`--enable-opt` and `--disable-opt` turn one of the linker's optimizations on or off. Squash passes
`--disable-opt ipconstprop`, so that a constant returned by a dependency is not folded into the
library. This turns it back on:

```xml
<PropertyGroup>
  <SquashExtraArgs>--enable-opt ipconstprop</SquashExtraArgs>
</PropertyGroup>
```

The linker describes `ipconstprop` under
[Using custom substitutions](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#using-custom-substitutions),
and one other optimization in
[Optimizations definitions](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/optimizations.md).


## Options that have a setting

Some options are better given through MSBuild, which Squash turns into the argument:

| Linker option | Use instead |
|---|---|
| [`--nowarn`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#turning-off-warnings) | `NoWarn` |
| [`--warnaserror`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#treat-warnings-as-errors) | `TreatWarningsAsErrors`, `WarningsNotAsErrors`, `SquashTreatWarningsAsErrors` |
| [`-x`](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/illink-options.md#trimming-from-an-xml-descriptor) | `SquashRootDescriptor` |

These are described in the [readme](/readme.md#linker-warnings).


## Options to leave alone

Squash depends on some of its own arguments, and replacing them breaks the build or the result:

 * `-a`, `-reference` and `-out`: the assembly, what it is resolved against, and where the task
   looks for the result.
 * `--action` and `--trim-mode`: only the project's own assembly is trimmed.
 * `--custom-step` and `--custom-data`: the steps that handle `InternalsVisibleTo` and write
   `removed.txt`.
