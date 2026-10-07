# Contributing

## Build and test

```
dotnet build src --configuration Release
dotnet run --project src/Squash.Tests --configuration Release --no-build -- --no-ansi --progress off
```

The integration suite consumes the package the Release build emits into `nugets`, so it runs second:

```
dotnet build IntegrationTests --configuration Release
dotnet run --project IntegrationTests/IntegrationTests --configuration Release --no-build -- --no-ansi --progress off
```

A single test:

```
dotnet run --project src/Squash.Tests --configuration Release --no-build -- --treenode-filter "/*/*/TrimTests/Survivors"
```


## Snapshots

Verify inlines snapshots under ten lines into the test source. A failing snapshot prints the received
value; accept it by pasting into the `.Snapshot(...)` call, or open the diff tool.

The `TrimTests.Survivors` and `TrimTests.Removed` snapshots record what the linker keeps and removes.
A change in one of them is a change in what Squash does to every library that uses it, so read the
diff before accepting it.


## Taking a new linker

The linker is the `Microsoft.NET.ILLink.Tasks` version in `src/Directory.Packages.props`.

A patch arrives as a Dependabot pull request and merges itself once the build passes.

A major version is taken by hand, since it raises the .NET runtime a build machine needs:

 * `ILLinkMajor` in `src/Directory.Build.props`.
 * The `Microsoft.NET.ILLink.Tasks` and `Microsoft.ILVerification` versions in
   `src/Directory.Packages.props`.
 * The SDK in `global.json` and `IntegrationTests/global.json`.
 * The runtime named in `readme.md` and in `docs/DiagnosticCodes.md`.

To try any linker version without changing a file:

```
dotnet build src --configuration Release -p:SquashCanaryILLinkVersion=11.0.0 -p:ILLinkMajor=11
```

`linker-canary.yml` does that every week for the newest prerelease and the newest daily build.


## Docs

`readme.md` and `docs/*.md` are maintained in place by MarkdownSnippets, which runs on any build of
`src/Squash.Tests`. Edit the prose directly, but never the content between `<!-- snippet: -->` and
`<!-- endSnippet -->`.
