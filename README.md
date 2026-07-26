# Klonker

Klonker is an open-source, Windows-first desktop developer tool for creating a
clean programming project from a reusable template. It is aimed first at
beginner developers who frequently need small, understandable projects for
trying ideas.

## Current status

The first functional vertical slice is implemented:

- a local JSON registry locates the development C++ CLI package;
- Core parses and validates the version-zero TOML manifest;
- string, boolean, and choice parameters support defaults and validation;
- restricted Scriban rendering creates a deterministic, path-safe in-memory
  preview;
- the Avalonia desktop app displays the sample, generated parameter controls,
  a directory tree, and selectable rendered text;
- Core can transactionally write a validated plan to a new or empty directory;
- automated tests cover manifests, values, rendering, paths, planning,
  execution, and headless view-model behavior.

The desktop UI currently stops at preview. It does not yet expose the Core
generation executor.

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

## Repository layout

```text
src/
  Klonker.Core/          Avalonia-free template and generation engine
  Klonker.Desktop/       Avalonia desktop UI and local sample catalog service
tests/
  Klonker.Core.Tests/    Core integration and headless view-model tests
samples/local-registry/  Development-only registry and template package
docs/                    Product and engineering documentation
eng/                     Validation, run, and clean scripts
```

## Development sample

`samples/local-registry` contains
`official.cpp-cli.windows-cmake`, a dependency-free C++ command-line starter.
It generates `CMakeLists.txt`, a project README, `src/main.cpp`, and a modest
argument parser supporting `--help`, `-h`, `--version`, unknown-option errors,
and positional arguments. The sample is test and development data, not the
future production official-template repository.

## Security

Template packages are untrusted. Klonker rejects unsafe and colliding Windows
paths and version zero rejects symbolic links/reparse points. Scriban receives
only declared primitive values and six deterministic Klonker string helpers;
it receives no filesystem, environment, network, process, reflection, clock,
or random access. Templates cannot supply commands or scripts.

## Documentation

- [Product scope](docs/product-scope.md)
- [Architecture](docs/architecture.md)
- [Development](docs/development.md)
- [Testing](docs/testing.md)
- [Template format v0](docs/template-format-v0.md)
- [Roadmap](docs/roadmap.md)
- [Architecture decisions](docs/decisions/)

## Short roadmap

Next, connect destination selection and the tested Core executor to the desktop
workflow. After that: local-registry configuration and package caching, richer
preview/validation, and then carefully scoped WSL generation. Remote registry
synchronization, modules, and agent integrations remain deferred.

Klonker is licensed under the [MIT License](LICENSE).
