# Development

## Required tools and observed environment

The repository requires a stable .NET 10 SDK and PowerShell. `global.json`
selects SDK 10.0.302 and allows later compatible feature bands.

The foundation was validated on Windows x64 (Windows build 10.0.28000) with:

- .NET SDK 10.0.302 and .NET runtime 10.0.10;
- the installed Avalonia MVVM template defaulting to Avalonia 12.1.0 and
  CommunityToolkit.Mvvm;
- no .NET workloads installed or required;
- no parent-directory `global.json`.

Different compatible .NET 10 patches/feature bands may be selected by the
roll-forward policy.

## Visual Studio

Open `Klonker.slnx` in a Visual Studio release that supports .NET 10. Install
.NET desktop tooling and the .NET 10 SDK. The Avalonia Visual Studio extension
is optional but useful for AXAML editing. Set `Klonker.Desktop` as the startup
project. C++ and CMake workloads are not required for Klonker development.

## Command-line workflow

```powershell
dotnet restore Klonker.slnx
dotnet build Klonker.slnx --configuration Debug
dotnet test Klonker.slnx --configuration Debug
dotnet run --project src/Klonker.Desktop/Klonker.Desktop.csproj
```

Scripts:

- `eng/validate.ps1`: restore, Debug build, Debug tests, and formatting check.
- `eng/run.ps1`: run the desktop project and forward trailing arguments.
- `eng/clean.ps1`: remove local build/test/generated-sample output only.

## Development sample lookup

On startup, `DevelopmentSampleRegistryLocator` walks upward from
`AppContext.BaseDirectory` and the current directory looking for
`samples/local-registry/registry.json`. This makes `dotnet run`, Visual Studio,
and test output work without embedding repository paths. A packaged catalog
service will replace this development-only behavior.

If the app reports that samples cannot be found, run it from a repository
checkout and confirm the registry file exists. If SDK selection fails, run
`dotnet --info` and `dotnet --list-sdks`; install a stable .NET 10 SDK if none
is available. If formatting validation fails, run `dotnet format Klonker.slnx`
and inspect the diff.

## Add a local sample package

1. Add `samples/local-registry/packages/<package-id>/template.toml`.
2. Put template payload under its `content/` directory.
3. Add one matching object to `samples/local-registry/registry.json`.
4. Keep registry and manifest identity/version fields identical.
5. Add parser, planning, expected-tree, and expected-content tests.
6. Run `eng/validate.ps1`.

Development samples must remain deterministic and must not require network
access or external build tools in Klonker tests.
