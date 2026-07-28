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
  TOML sources and create deterministic ZIPs, registry version 1,
  generated Markdown/JSON catalogs, and an optional detached signature.

The main repository's `Nightly Build` workflow runs validation on every push
to `main`, publishes Desktop as a self-contained single-file Windows x64
executable, uploads a short-lived workflow artifact, and replaces the stable
`nightly` prerelease asset. To reproduce the publish stage locally:

```powershell
dotnet publish .\src\Klonker.Desktop\Klonker.Desktop.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false
```

GitHub supplies `GITHUB_TOKEN`; no personal secret is required. Repository or
organization policy must permit Actions to write repository contents so the
workflow can move the `nightly` tag and update its prerelease.

## Registry configuration and development lookup

On first startup, Desktop creates:

```text
%LOCALAPPDATA%\Klonker\registries.json
%LOCALAPPDATA%\Klonker\settings.json
%LOCALAPPDATA%\Klonker\favorites.json
%LOCALAPPDATA%\Klonker\cache\
%LOCALAPPDATA%\Klonker\logs\
```

`DevelopmentSampleRegistryLocator` walks upward from `AppContext.BaseDirectory`
and the current directory looking for `samples/local-registry/registry.json`;
when found, its absolute path is written as the first local source. The Core
library never contains this repository path.

Use the in-app Settings window to add local index paths or HTTPS remote index
URLs, configure offline mode, and pin publisher keys. The JSON files remain
inspectable for diagnostics but normally should not be edited by hand. New
configurations include the canonical signed official index at
`https://raw.githubusercontent.com/klonkerdev/registry/main/dist/registry.json`.
To test a staging endpoint before the first launch, override it:

```powershell
$env:KLONKER_OFFICIAL_REGISTRY_URL =
  'https://staging.example/registry.json'
.\eng\run.ps1
```

Once `registries.json` exists, the environment variable does not overwrite
user configuration. See `docs/registry-format-v1.md` and
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

1. Add `samples/local-registry/packages/<package-id>/template.toml`, including
   a lowercase `language` identifier and an explicit build system (`none` is
   valid).
2. Put template payload under its `content/` directory.
3. Calculate its canonical integrity metadata:

   ```powershell
   .\eng\get-package-integrity.ps1 `
     -PackageRoot .\samples\local-registry\packages\<package-id>
   ```

4. Add one matching object to `samples/local-registry/registry.json`, including
   the reported `package_sha256` and `package_size_bytes`.
5. Keep registry and manifest identity/version fields identical.
6. Run `eng/validate.ps1`. It discovers every sample registry and validates
   every referenced package without a package-specific branch.

Add focused behavioral tests only when the format, security boundary,
rendering behavior, or generator behavior changes. Adding another data-only
sample does not require a special test case.

Development samples must remain deterministic and must not require network
access or external build tools in Klonker tests.
