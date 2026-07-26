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
- `eng/get-package-integrity.ps1`: calculate the canonical digest/size for an
  editable local package directory.
- `eng/pack-registry.ps1`: discover hierarchical namespace/package/variant
  TOML sources and create deterministic ZIPs plus registry version 1 output.

## Registry configuration and development lookup

On first startup, Desktop creates:

```text
%LOCALAPPDATA%\Klonker\registries.json
%LOCALAPPDATA%\Klonker\cache\
```

`DevelopmentSampleRegistryLocator` walks upward from `AppContext.BaseDirectory`
and the current directory looking for `samples/local-registry/registry.json`;
when found, its absolute path is written as the first local source. The Core
library never contains this repository path.

Edit `registries.json` to add absolute or configuration-relative local index
paths and HTTPS remote index URLs. Set `offline` to `true` to prohibit HTTP and
use only validated cache entries. New configurations include the canonical
official index at
`https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json`.
To test a staging endpoint before the first launch, override it:

```powershell
$env:KLONKER_OFFICIAL_REGISTRY_URL =
  'https://staging.example/registry.json'
.\eng\run.ps1
```

Once `registries.json` exists, edit it directly; the environment variable does
not overwrite user configuration. See `docs/registry-format-v1.md` and
`samples/registry-configuration.example.json`.

If the app reports that samples cannot be found, run it from a repository
checkout, confirm the registry file exists, and inspect the configuration path
shown by the catalog. Delete or repair a malformed `registries.json`; do not
delete the package cache merely for an index network failure because Klonker
can use the validated cached index. If SDK selection fails, run `dotnet --info`
and `dotnet --list-sdks`; install a stable .NET 10 SDK if none is available.
If formatting validation fails, run `dotnet format Klonker.slnx` and inspect
the diff.

## Add a local sample package

1. Add `samples/local-registry/packages/<package-id>/template.toml`.
2. Put template payload under its `content/` directory.
3. Calculate its canonical integrity metadata:

   ```powershell
   .\eng\get-package-integrity.ps1 `
     -PackageRoot .\samples\local-registry\packages\<package-id>
   ```

4. Add one matching object to `samples/local-registry/registry.json`, including
   the reported `package_sha256` and `package_size_bytes`.
5. Keep registry and manifest identity/version fields identical.
6. Add parser, planning, expected-tree, and expected-content tests.
7. Run `eng/validate.ps1`.

Development samples must remain deterministic and must not require network
access or external build tools in Klonker tests.
