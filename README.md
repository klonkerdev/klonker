# Klonker

Klonker is an open-source, Windows-first desktop developer tool for creating a
clean programming project from a reusable template. It is aimed first at
beginner developers who frequently need small, understandable projects for
trying ideas.

## Current status

The first generation and registry vertical slices are implemented:

- registry schema version 1 qualifies identities by registry and requires
  package SHA-256/size metadata;
- `%LOCALAPPDATA%\Klonker\registries.json` configures local and HTTPS remote
  sources outside the repository;
- validated remote indexes and packages use a transactional local cache with
  explicit offline mode and cached-index fallback;
- Core parses and validates the version-zero TOML manifest;
- string, boolean, and choice parameters support defaults and validation;
- restricted Scriban rendering creates a deterministic, path-safe in-memory
  preview;
- the high-contrast charcoal Avalonia catalog first groups registry entries
  into package cards, with name/language/platform/build-system/tag filters;
- confirming a package fills a second card list with only that package's
  variants; confirming a variant opens the configuration, preview, and
  generation screen;
- variant cards pair readable labels with host-owned platform and build-system
  logos for Windows, Linux, CMake, GNU Make, and xmake;
- package cards show host-owned C++ or Lua language marks; variants that do
  not use a build system present their own purpose instead of a meaningless
  build badge;
- the configuration screen provides generated parameter controls, the reusable
  project-tree control, and selectable rendered text;
- optional package logos and arbitrary descriptive tags appear on package and
  variant cards, while session favorite toggles remain variant-specific;
- custom tags use a distinct colored row and catalog filter; platform,
  language, and build metadata remain separate;
- generated C++, Lua, CMake, Markdown, and configuration text uses a
  selectable, read-only syntax-highlighted preview;
- preview navigation includes direct file selection, previous/next actions,
  and expand/collapse-all controls;
- copied known-text source files are strictly decoded as UTF-8 for preview
  while remaining byte-for-byte copies in the generation plan;
- manifests can declare display-only after-generation prerequisites;
- Desktop provides a destination field/native folder picker, explicit
  confirmation, transactional Generate action, cancellation, and structured
  success/failure details;
- Core writes a validated plan only to a new or empty directory;
- automated tests cover manifests, values, rendering, paths, planning,
  registry integrity/cache/offline behavior, execution, and headless
  view-model behavior.

## Version-one scope

Version one is a project generator. It will discover templates from configured
registries, cache packages for offline use, show independently versioned
variants, validate configuration and known prerequisites, preview output, and
generate into a new or empty destination. Windows destinations come first;
generation inside a selected WSL distribution is planned.

Klonker does **not** build generated projects, run build tools, install SDKs,
execute arbitrary template scripts, manage Git, import existing projects,
merge into non-empty trees, or update a project after generation. It is not a
build system or package manager. Generated projects are entirely detached from
Klonker and belong to the user.

## Prerequisites

- Windows for the supported desktop development path
- .NET 10 SDK (the repository selects SDK 10.0.302 with compatible
  feature-band roll-forward)
- PowerShell 7 or Windows PowerShell 5.1 for repository scripts
- Visual Studio with .NET desktop tooling is optional; the `dotnet` CLI is
  sufficient

No C++ compiler or CMake installation is needed to build or test Klonker.
Generated C++ projects still require a compatible compiler toolchain when the
user later chooses to build them.

## Nightly Build

Every successful push to `main` validates the repository and replaces the
[`nightly` prerelease](https://github.com/klonkerdev/klonker/releases/tag/nightly)
asset with a self-contained Windows x64 executable:
`Klonker-nightly-win-x64.exe`.

Nightlies are unsigned development snapshots. Windows may show a SmartScreen
warning, and no stability guarantee is implied. The release notes include the
source commit and SHA-256 digest. The executable does not require a separately
installed .NET runtime.

## Develop locally

```powershell
dotnet restore Klonker.slnx
dotnet build Klonker.slnx --configuration Debug
dotnet test Klonker.slnx --configuration Debug
dotnet format Klonker.slnx
.\eng\validate.ps1
.\eng\run.ps1
```

Use `.\eng\clean.ps1` to remove repository-local `bin`, `obj`, `TestResults`,
and known generated sample output.

To build deterministic ZIP/index artifacts for a separate registry repository:

```powershell
.\eng\pack-registry.ps1 `
  -SourceRoot D:\repos\klonker-registry `
  -OutputRoot D:\repos\klonker-registry-dist
```

The source root uses `registry.toml` plus discovered
`templates/<namespace>/<package>/variants/<variant>` folders; template entries
are not maintained by hand. See
[Official registry](docs/official-registry.md).

## Repository layout

```text
src/
  Klonker.Core/          Avalonia-free template and generation engine
  Klonker.Desktop/       Avalonia desktop UI and configured catalog service
tests/
  Klonker.Core.Tests/    Core integration and headless view-model tests
samples/local-registry/  Development-only registry and template package
samples/official-registry-repository/  External-repository metadata seed
docs/                    Product and engineering documentation
eng/                     Validation, run, clean, and registry packaging scripts
```

## Development sample

`samples/local-registry` contains
`std.cpp-cli.windows-cmake`, a dependency-free C++ command-line starter.
It generates `CMakeLists.txt`, a project README, `src/main.cpp`, and a modest
argument parser supporting `--help`, `-h`, `--version`, unknown-option errors,
and positional arguments. The checked-in sample is test and development data;
the production catalog is maintained separately in the
[`klonkerdev/registry`](https://github.com/klonkerdev/registry) repository and
currently publishes Windows/Linux variants for CMake, GNU Make, and xmake.
The prepared registry source also contains five `gof2.modapi` Lua starters for
events, ImGui menus, rendering hooks, campaign missions, and custom content;
they use `build_system = "none"` and are published when the registry
repository's updated `dist/` is pushed. New Klonker configurations use the
published raw GitHub index directly.

## Security

Template packages are untrusted. Klonker rejects unsafe and colliding Windows
paths and version zero rejects symbolic links/reparse points. Scriban receives
only declared primitive values and six deterministic Klonker string helpers;
it receives no filesystem, environment, network, process, reflection, clock,
or random access. Manifests cannot request generator commands, setup scripts,
or lifecycle hooks. A template may contain source-code files such as Lua, but
Klonker treats them as data and never executes them. Remote registries are
HTTPS-only; packages are size-limited, SHA-256 verified, and extracted through
the same Windows path-safety boundary. Checksums provide integrity, not
publisher authentication.

## Documentation

- [Product scope](docs/product-scope.md)
- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [Testing](docs/testing.md)
- [Template format v0](docs/template-format-v0.md)
- [Registry format v1](docs/registry-format-v1.md)
- [Official registry preparation](docs/official-registry.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decisions](docs/decisions/)

## Short roadmap

Next, push the prepared GOF2 registry artifacts, add persistent favorite
preferences and registry management UI, and define a signed-registry trust
policy. WSL generation, modules, and agent integrations remain deferred.

Klonker is licensed under the [MIT License](LICENSE).
