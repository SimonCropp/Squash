Removes unreachable IL from a library at build time, using the .NET IL linker (illink). The public
surface is untouched; non-public types and members nothing reaches are trimmed.

```xml
<PackageReference Include="Squash" Version="x.y.z" PrivateAssets="all" />
```

See https://github.com/SimonCropp/Squash
