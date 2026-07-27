# Klonker repository guidance

## Purpose and boundaries

Klonker is a Windows-first desktop tool that creates new programming projects
from reusable templates. Version one is a generator: it discovers templates,
collects values, validates and previews output, then generates into a new or
empty directory.

Klonker does not build, run, update, import, or manage generated projects. It
does not install SDKs, initialize Git, execute template scripts, or merge into
non-empty trees. Local/HTTPS registries, package integrity checks, offline
caching, preview, transactional Windows generation, and the external official
registry are implemented. WSL generation, signatures, and modules are planned.
Generated projects are fully detached from Klonker.

## Repository structure

- `src/Klonker.Core`: Avalonia-free parsing, validation, rendering, path
  safety, planning, registry-index reading, and filesystem execution.
- `src/Klonker.Desktop`: Avalonia views, view models, user registry
  configuration, native destination selection, and desktop services.
- `tests/Klonker.Core.Tests`: behavioral and temporary-directory integration
  tests for Core and headless view-model tests.
- `samples/local-registry`: development registry and C++ CLI sample package.
- `docs`: product, architecture, format, development, testing, roadmap, and
  decision records.
- `eng`: PowerShell development scripts.

Dependency direction is Desktop -> Core and Tests -> Core/Desktop. Core must
never reference Avalonia or repository-specific sample locations. Keep the
small object graph explicit; do not add a DI container, service locator,
plugin framework, process runner, or speculative project split.

## Commands

Run from the repository root:

```powershell
dotnet restore Klonker.slnx
dotnet build Klonker.slnx --configuration Debug
dotnet test Klonker.slnx --configuration Debug
dotnet format Klonker.slnx
dotnet format Klonker.slnx --verify-no-changes
.\eng\validate.ps1
.\eng\run.ps1
.\eng\clean.ps1
.\eng\get-package-integrity.ps1 -PackageRoot <package-directory>
.\eng\pack-registry.ps1 -SourceRoot <registry-source> -OutputRoot <new-output>
```

Run `eng/validate.ps1` before declaring work complete.

## Architecture and implementation rules

- Inspect relevant production code and behavioral tests before changing
  behavior.
- Keep template parsing, rendering, path safety, planning, and writes out of
  views and view-models.
- Use immutable public domain collections and deterministic ordinal ordering.
- Use structured validation issues for expected user/template errors and
  exceptions for unexpected I/O failures.
- Keep code-behind limited to initialization and truly view-specific behavior.
- Prefer small, cohesive, reviewable changes and interfaces only at genuine
  boundaries.
- Do not claim unimplemented features in code, README, or docs.

## Template and filesystem security

Treat registries and packages as untrusted input. Scriban receives only
declared primitive values and Klonker-owned deterministic helpers. Never expose
filesystem, environment, network, clock, random, reflection, process, or
arbitrary .NET access. Never execute template-provided commands, generator
hooks, setup scripts, build tools, source-code payloads, or generated
programs. Templates may generate source such as Lua, but it remains inert data
to Klonker.

Validate source and rendered paths using both `/` and `\` as separators.
Reject rooted, UNC, drive-qualified, traversal, NUL-containing, invalid, and
reserved Windows paths. Compare destinations case-insensitively, detect
file/directory collisions, reject reparse points, and repeat a containment
check at the final filesystem resolution point. Never rely on string-prefix
containment checks or overwrite an existing file.

Remote registry indexes must use HTTPS. Do not cache an index until it parses
successfully. Enforce byte limits, verify package size and SHA-256 before
extraction, use opaque hash-derived cache paths, reject unsafe ZIP entries, and
install cache entries transactionally. Offline mode must perform no network
requests. Checksums are integrity controls, not signature/trust controls.

Official registry source uses
`templates/<namespace>/<package>/variants/<variant>`. The publisher discovers
`package.toml` and `variant.toml`, derives IDs from matching folder names, and
generates the runtime manifest and JSON index. Do not reintroduce a handwritten
template array or a flat directory per variant.

## Testing and documentation

Add or update behavioral tests whenever behavior changes. Security boundaries
need focused negative tests. Filesystem tests must use unique temporary
directories, avoid sleeps and network/toolchain dependencies, and clean up.
View-model behavior should stay testable without opening a native window.

Keep `README.md` and `docs/` aligned with implemented behavior. Update
`docs/template-format-v0.md` when parsing/rendering rules change,
`docs/architecture.md` when dependencies or pipeline boundaries change, and
`docs/roadmap.md` when milestones move. Do not describe planned behavior as
available.
