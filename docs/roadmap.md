# Roadmap

## Completed foundation

- [x] .NET 10 solution with Core, Avalonia Desktop, and xUnit projects
- [x] centralized packages, analyzers, formatting, scripts, and documentation
- [x] version-zero TOML manifest and version-one JSON registry reader
- [x] typed parameters, defaults, choices, and C++ identifier validation
- [x] restricted deterministic Scriban helpers
- [x] Windows path safety, reparse rejection, and immutable planning
- [x] transactional Core filesystem executor
- [x] development C++ CLI/CMake template
- [x] automated Core, executor, sample-output, and view-model coverage

## Current vertical slice

- [x] locate the checked-in development registry on desktop startup
- [x] group independently versioned registry entries into package cards
- [x] filter packages and variants by name, language, platform, build system,
      and custom tag
- [x] confirm a package before filling its dedicated variant list
- [x] confirm a variant before opening configuration, preview, and generation
- [x] render platform/build-system marks on known variant cards
- [x] render language marks on package cards and omit build marks for
      `build_system = "none"`
- [x] use custom charcoal window chrome and the Klonker logo asset
- [x] use host-owned vector icons instead of font glyphs for UI actions
- [x] show generated configuration controls
- [x] build a Core generation plan
- [x] show a reusable hierarchical directory tree with semantic file icons
- [x] retain independent expansion state for each directory node
- [x] select a generated tree file to inspect its rendered text
- [x] show host-controlled syntax highlighting for known preview file types
- [x] support package logos and custom tags
- [x] provide persistent app-local favorite, hover, and selected card states
- [x] filter by the union of custom manifest tags across the active catalog
- [x] preview copied known-text sources through strict UTF-8 decoding
- [x] present loading, validation, and rendering failures without crashing
- [x] choose a destination and invoke the tested executor from Desktop
- [x] require explicit confirmation before generation
- [x] show generation success/failure and safe diagnostic details
- [x] display declared after-generation prerequisites
- [x] navigate preview files directly and expand/collapse the tree

## Registry and offline slice

- [x] configure local registry locations outside the repository
- [x] define package cache layout and zero-network offline behavior
- [x] add package SHA-256/size and registry-qualified template identity
- [x] synchronize HTTPS registry indexes with validated-cache fallback
- [x] verify and safely extract cached remote ZIP packages
- [x] prepare deterministic publishing artifacts for a separate registry repo
- [x] prepare the standalone `klonkerdev/registry` source, CI, and distribution
- [x] discover hierarchical namespace/package/variant registry sources
- [x] publish language metadata in runtime manifests and registry indexes
- [x] publish multiple independently versioned native and scripting template
  families through the discovered registry hierarchy
- [x] add a validated self-contained Windows x64 nightly release workflow
- [x] add an in-app registry/application settings editor
- [x] add an in-app About window with build, project, license, and author
  information
- [x] persist user favorites outside template manifests
- [x] verify detached registry signatures with publisher trust and key
  rotation/revocation
- [x] protect the registry `main` branch with required generic validation and
  code-owner review
- [x] add active, consented, read-only prerequisite probes without installing
  or executing tools
- [x] generate repository Markdown and static-site JSON catalogs from
  discovered registry sources
- [x] add a guided template-authoring wizard with data-driven language,
  build-system, platform, and license choices
- [x] inspect existing runtime/source packages and safely convert ordinary
  code folders into detached registry-source packages
- [x] support platform-independent `any` targets and multi-build
  platform/build-system variant matrices
- [x] derive detached authoring packages from loaded catalog templates
- [x] add a registry workspace wizard for generic local builds, app-local
  development registration, production signing setup, and key-safe rotation
  guidance
- [x] add deterministic registry source/version conflict policy with stable
  preference, prerelease opt-in, and app-local exact pins
- [x] generate projects and modules inside a selected running WSL
  distribution with read-back verification
- [x] add separately indexed reusable modules, configurable path slots,
  non-empty-tree preflight, post-generation instructions, and dependency
  license aggregation
- [x] add app-local favorite/curated template or module catalog tabs

## Next milestones

- [x] publish the prepared `klonkerdev/registry` GitHub repository
- [x] validate a clean online download and zero-network cached replay
- [x] add Windows/Linux CMake, GNU Make, and xmake C++ CLI variants

## Deferred

- [ ] additional official template families in the separate registry
- [ ] plugins, MCP, or coding-agent integration
- [ ] any project build, execution, import, update, or management behavior
