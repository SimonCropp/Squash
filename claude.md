# Squash

An MSBuild task package that trims a library's own assembly straight after the compiler has run. It
runs the .NET IL linker (illink) over the intermediate assembly in library mode, where the visible
surface is the root, and puts the result back in `obj`, so everything later in the build sees only
the trimmed assembly. The linker is not referenced or restored by consumers: a pinned copy travels
inside the package.

## Layout

- `src/Squash` - the task package. `netstandard2.0` only. Its build stages the linker into
  `bin/{Configuration}/illink`, which is what the package carries and what the tests run.
- `src/Squash.Steps` - five linker steps, loaded by the linker through `--custom-step`. Targets the
  linker's own framework, `net$(ILLinkMajor).0`.
- `src/Fixtures` - libraries the behaviour tests trim. Outside the repository's build settings on
  purpose.
- `src/Squash.Tests` - unit tests, and behaviour tests that run `SquashTask` directly against the
  built fixtures with no MSBuild involved.
- `IntegrationTests/` - a separate solution that runs real builds of fixture projects against the
  nupkg the `src` Release build emits into `nugets/`, so `dotnet build src -c Release` must run first.

## Conventions

- `ProjectDefaults` supplies packaging metadata, nullable, implicit usings and code style. Never
  hand-edit `.editorconfig` or `Shared.sln.DotSettings`.
- One `NoWarn` property in `src/Directory.Build.props`.
- Tests are TUnit plus Verify, run with
  `dotnet run --project <project> -c Release --no-build -- --no-ansi --progress off`.
- No `return c ? a : b;` or `=> c ? a : b`: an `if` that returns, then the fallback return. No
  `else` after a branch that returns.
- Docs are checked by MarkdownSnippets' content validation, which rejects second-person and filler
  words.

## Rules

- The task assembly stays `netstandard2.0` and ships alone. A framework-specific asset selected on
  `$(MSBuildRuntimeType)` only loads on the newest SDK, and a dependency would load into every
  MSBuild process. A test checks the folder holds one assembly.
- The task knows the linker only through its command line, built in `ResponseFile`. Do not load
  `illink.dll`, and do not reuse the `ILLink` MSBuild task: both have broken across linker releases
  where the command line has not.
- The steps use only the linker's reference contract: `BaseStep`, `IMarkHandler`, `MarkContext`,
  `LinkContext.Resolve`, `GetLoadedAssembly` and `TryGetCustomData`, and `AnnotationStore.IsMarked`,
  `SetPreserve` and `AddPreservedMethod`. The implementation exposes far more, and it changes
  between majors.
- Rooting stays the linker's. The `InternalsVisibleTo` steps only take the attributes away before
  the linker's root step and put them back before marking; they do not mark anything.
- `Squash_InternalNamespacesToKeep` becomes a root descriptor of exact `<namespace>` entries, written on
  each trim from the namespaces `Namespaces.Read` finds in the compiled assembly. It is never
  written for no namespaces: the linker reads an `<assembly>` element with no children as the whole
  assembly. Nothing is marked by a step for it.
- An entry leaves the documentation file only when its name is in `removed-documentation.txt`,
  which `RemovedDocumentation` writes after the linker's output: the names of everything in the
  assembly under `in/`, less the names of everything in the one under `out/`, both from
  `DocumentationIds`. Nothing is taken from marking. A name spelled differently from the compiler's
  therefore matches nothing and its entry stays; no spelling mistake can remove the entry of a
  member that is still there.
- `DocumentationIds` spells names as the C# compiler does, and reads the files with
  `System.Reflection.Metadata`, not Cecil. Every summary in the `Documented` fixture says `Kept.` or
  `Removed.`, and `DocumentationTests.OnlyWhatIsLeftIsStillDocumented` holds the trimmed file to
  that. A shape of member the fixture lacks goes into it, in a type that is removed.
- `Documentation.Trim` only leaves bytes out. It never parses an entry and writes it again, so what
  stays is what the compiler wrote; `DocumentationTests.WhatIsLeftIsExactlyAsTheCompilerWroteIt`
  checks that against a second way of doing the same.
- `--custom-data` values carry no path. The linker splits them on every `=`, so the steps get bare
  file names and resolve them against the working directory the task sets.
- `-a` is given the assembly name, never a path, and the assembly is staged as
  `in/{AssemblyName}.dll`. Newer linkers accept only names, resolved through `-reference`.
- Each default argument in `ResponseFile` answers a specific failure, noted beside it. Removing one
  needs the test that covers it to change.
- The intermediate assembly is moved into `in/`, not copied. A failed trim then leaves nothing in
  `obj` for the build to ship, and the next build compiles again.
- Success is the linker's exit code and an error-free log, decided before anything is moved back.
  The linker writes its output before it reports warnings promoted to errors.
- `_Squash_Prepare` runs before every real compile, enabled or not. It always writes `inputs.txt`,
  and lists everything under `obj/.../Squash` in `FileWrites` on every build; otherwise
  `IncrementalClean` deletes those files on the builds that do not trim.
- `SquashAssembly` is appended to `TargetsTriggeredByCompilation` inside `_Squash_Prepare`, at
  execution. Appended at evaluation it would run before Fody, whose weavers then look for members
  already trimmed.
- Every setting passed to `SquashTask` is recorded in `inputs.txt`, and `Squash.targets` passes
  exactly the task's public properties; tests check both.
- No project under `src` may set `IsTrimmable`, `IsAotCompatible` or `EnableTrimAnalyzer`. The SDK
  would add its own reference to `Microsoft.NET.ILLink.Tasks`, which NuGet rejects (NU1009) beside
  the central version.
- Fixtures do not reference `ProjectDefaults`, always compile optimized, and declare
  `InternalsVisibleTo` in source. Their IL has to be the same in a Debug and a Release test run,
  since the snapshots record method sizes.
- `TrimTests.Survivors` and `TrimTests.Removed` are the contract with the linker. Never accept a
  change to one without knowing which linker or fixture change caused it.
- `ValidityTests` hold for any trimmed assembly: visible surface untouched, surviving bodies
  unchanged in size, valid IL, every method compiles, symbols match. A new fixture or setting joins
  `Trims.All()`.
- The linker's major version is `ILLinkMajor`, the package version, and both `global.json` files,
  moved together by hand. Dependabot is told to leave the major alone; see `contributing.md`.
- Diagnostic codes are never reused. Adding or changing one means updating
  `docs/DiagnosticCodes.md` in the same change; a test checks every code has a section.
