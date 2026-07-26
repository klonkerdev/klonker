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
- [x] use custom charcoal window chrome and the Klonker logo asset
- [x] use host-owned vector icons instead of font glyphs for UI actions
- [x] show generated configuration controls
- [x] build a Core generation plan
- [x] show a reusable hierarchical directory tree with semantic file icons
- [x] retain independent expansion state for each directory node
- [x] select a generated tree file to inspect its rendered text
- [x] show host-controlled syntax highlighting for known preview file types
- [x] support package logos, custom tags, and initial favorite metadata
- [x] provide interactive favorite, hover, and selected variant-card states
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

## Next milestones

- [ ] add an in-app registry source/settings editor
- [ ] persist user favorites outside template manifests
- [ ] add registry signatures, publisher trust, and key rotation
- [x] publish the prepared `klonkerdev/registry` GitHub repository
- [x] validate a clean online download and zero-network cached replay
- [ ] protect the registry `main` branch with required validation and review
- [x] add Windows/Linux CMake, GNU Make, and xmake C++ CLI variants
- [ ] add active, consented prerequisite probes without installing tools

## Deferred

- [ ] advanced registry conflict/version-selection policy
- [ ] generation inside a selected WSL distribution
- [ ] reusable modules/slots and dependency-license aggregation
- [ ] additional official template families in a separate repository
- [ ] plugins, MCP, or coding-agent integration
- [ ] any project build, execution, import, update, or management behavior
