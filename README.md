# Klonker

Klonker is an open-source, Windows-first desktop developer tool for creating a
clean programming project from a reusable template. It is aimed first at
beginner developers who frequently need small, understandable projects for
trying ideas.

## Current status

The first generation and registry vertical slices are implemented:

- registry schema version 1 qualifies identities by registry and requires
  package SHA-256/size metadata;
- registries index templates and reusable modules separately, and the catalog
  resolves multiple published versions using newest-stable/newest-any policy
  or exact app-local pins;
- `%LOCALAPPDATA%\Klonker\registries.json` configures local and HTTPS remote
  sources, publisher trust, and rotated/revoked keys outside the repository;
- the official registry index is verified against an app-pinned detached
  RSA signature before it can enter the validated cache;
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
  variant cards, while favorites persist only in the app-local
  `%LOCALAPPDATA%\Klonker\favorites.json` store;
- custom tags use a distinct colored row and catalog filter; platform,
  language, and build metadata remain separate;
- generated C++, Lua, CMake, Markdown, and configuration text uses a
  selectable, read-only syntax-highlighted preview;
- preview navigation includes direct file selection, previous/next actions,
  and expand/collapse-all controls;
- copied known-text source files are strictly decoded as UTF-8 for preview
  while remaining byte-for-byte copies in the generation plan;
- manifests can declare after-generation prerequisites; explicitly consented
  checks inspect host-owned PATH/known-folder probes without installing tools
  or executing template commands;
- an in-app settings window edits registry sources and publisher keys,
  appearance, diagnostics, system behavior, and local cache/preferences;
- an About window shows the running build version, platform/runtime details,
  project purpose, repository documentation, MIT license, and author
  [@SleathCobra](https://github.com/SleathCobra);
- an animated in-app template wizard creates registry-source packages from
  configurable language/build-system/platform cards, including `Any platform`
  and multi-build variant matrices, inspects existing folders, or derives a
  detached editable package from a loaded catalog template;
- the wizard validates `template.toml` runtime packages and
  `package.toml`/`variants/` registry sources, reports actionable findings,
  previews every planned authoring file, and never edits the inspected tree;
- an in-app registry workspace wizard creates development or production
  source trees, imports authoring packages, discovers and builds all variants
  generically, registers local development indexes, and creates detached
  publisher signatures with private keys kept outside the workspace;
- Desktop provides a destination field/native folder picker, explicit
  confirmation, transactional Generate action, cancellation, and structured
  success/failure details;
- Core writes a validated plan only to a new or empty directory;
- a separately indexed module can add a preflighted file tree to an existing
  project without overwriting any path, while presenting dependency licenses
  and inert post-generation instructions;
- projects and modules can target a selected running WSL distribution; the
  destination is transferred through the Windows WSL provider and every file
  is read back and compared with the preview;
- the catalog has separate Templates and Modules views plus app-local
  favorite or curated tabs created with `+`;
- automated tests cover manifests, values, rendering, paths, planning,
  registry integrity/cache/offline behavior, execution, and headless
  view-model behavior.

## Version-one scope

Version one is a project and reusable-module generator with detached template
and registry-authoring assistants. It discovers templates and modules from
configured registries, caches packages for offline use, selects independently
published versions, validates configuration and consented known
prerequisites, previews output, and generates on Windows or in a selected
running WSL distribution. Project templates retain the new/empty destination
rule. Modules are the narrow additive exception: they may target a non-empty
tree only after a complete no-overwrite conflict preflight. The authoring
wizard can create or inspect template source packages without managing the
source project.

Klonker does **not** build generated projects, run build tools, install SDKs,
execute arbitrary template/module scripts, manage Git, adopt existing
projects, merge project templates into non-empty trees, or update generated
content after generation. Existing-folder authoring is read-only inspection
plus an optional detached content copy. Module installation adds only its
precomputed, currently absent paths; it does not merge or modify existing
files. Klonker is not a build system or dependency package manager. Generated
projects and module files are entirely detached from Klonker and belong to
the user.

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
  -OutputRoot D:\repos\klonker-registry-dist `
  -SigningKeyPath D:\secure\publisher-key.pem
```

The source root uses `registry.toml` plus discovered
`templates/<namespace>/<package>/variants/<variant>` and
`modules/<namespace>/<module>` folders; index entries are not maintained by
hand. See
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

## Development and official catalogs

Checked-in sample registries are development/test data. Validation discovers
every sample `registry.json` and verifies all referenced package identities,
paths, checksums, and sizes without naming a particular template in the
pipeline. The production catalog is maintained separately in
[`klonkerdev/registry`](https://github.com/klonkerdev/registry); its
[generated Markdown catalog](https://github.com/klonkerdev/registry/blob/main/dist/catalog.md)
and `catalog.json` are rebuilt from package/variant manifests, so this README
does not need a handwritten package list.

## Security

Template packages are untrusted. Klonker rejects unsafe and colliding Windows
paths and version zero rejects symbolic links/reparse points. Scriban receives
only declared primitive values and six deterministic Klonker string helpers;
it receives no filesystem, environment, network, process, reflection, clock,
or random access. Manifests cannot request generator commands, setup scripts,
or lifecycle hooks. A template may contain source-code files such as Lua, but
Klonker treats them as data and never executes them. Remote registries are
HTTPS-only; packages are size-limited, SHA-256 verified, and extracted through
the same Windows path-safety boundary. A detached signature authenticates the
exact index bytes against app-local publisher trust. Multiple pinned keys and
explicit revoked-key records support publisher key rotation.

## Documentation

- [Product scope](docs/product-scope.md)
- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [Testing](docs/testing.md)
- [Template format v0](docs/template-format-v0.md)
- [Module format v0](docs/module-format-v0.md)
- [Registry format v1](docs/registry-format-v1.md)
- [WSL generation](docs/wsl-generation.md)
- [Official registry preparation](docs/official-registry.md)
- [Template authoring wizard](docs/template-authoring.md)
- [Registry workspace wizard](docs/registry-workspaces.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decisions](docs/decisions/)

## Short roadmap

Next work focuses on broader catalog coverage, usability refinement, and
optional agent integrations.

Klonker is licensed under the [MIT License](LICENSE).
