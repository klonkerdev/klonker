# Testing

## Layers

- **Unit behavior:** manifest shape, parameters, deterministic helpers,
  restricted rendering, and path normalization.
- **Package/planning integration:** temporary packages plus the checked-in C++
  sample validate enumeration, rendering, ordering, collision handling, and
  the no-write planning invariant.
- **Filesystem integration:** unique temporary directories validate new/empty
  destinations, non-empty refusal, containment, cancellation, and staging
  cleanup.
- **Registry integration:** in-memory HTTP handlers and temporary caches
  validate schema/version behavior, qualified identity, directory digests,
  package SHA-256/size, safe ZIP extraction, online caching, cached-index
  fallback, and zero-network offline reuse.
- **Desktop logic:** the single test project references Desktop and exercises
  catalog failure, registry-qualified package grouping, package/variant
  navigation, filters, platform/build-system presentation, tree construction,
  independent folder expansion, semantic file-icon classification, tree
  selection, manifest presentation metadata, syntax tokenization, favorites,
  custom-tag filtering/color assignment, strict copied-source decoding, and
  preview behavior, destination picking, explicit generation confirmation,
  structured diagnostics, prerequisites, and preview navigation without
  launching Avalonia windows.

Run:

```powershell
dotnet test Klonker.slnx --configuration Debug
.\eng\validate.ps1
```

Tests do not require external network access, a C++ compiler, CMake, WSL, Git,
or native UI. Remote-registry tests use deterministic in-memory HTTP responses.

## Temporary-directory policy

Every filesystem test creates a GUID-named directory under the operating
system temp path and deletes it through `IDisposable`. Tests may write only
inside that directory or a checked-in package created for the test. Avoid
shared names, arbitrary sleeps, and reliance on enumeration order.

## Security and determinism coverage

Path tests must cover both directory separators, traversal, rooted/UNC/drive
paths, NUL and invalid segments when relevant, reserved Windows devices,
case-insensitive duplicate destinations, file/directory collisions, rendered
separator injection, and safe nesting. Reparse-point behavior should be tested
where the test host can create links without elevation.

Planning tests compare ordered paths and immutable bytes across runs and assert
expected sample CMake and C++ content. Executor tests assert no overwrite and
that no output escapes the destination. Registry archive tests must include
traversal, case collisions, file/directory collisions, links where practical,
checksum mismatch, and offline cache misses.

## UI policy

Prefer headless view-model tests for catalog/configuration/preview state.
Manual UI smoke testing is required after changes to AXAML, bindings, startup
catalog discovery, selection behavior, or theme/layout. Verify that registry
variants initially appear as package groups, filters update those groups,
confirming a package shows only its variants, platform/build-system marks are
correct, confirming a variant opens configuration, and Back returns to the
variant list. Then verify invalid configuration is readable, preview builds,
folders expand, file-type icons are distinguishable, collapsing one folder
does not collapse another, card hover/selection/favorite states are visible,
selecting a tree file changes syntax-highlighted content, preview navigation
works, destination selection opens, confirmation precedes generation, a
successful result is visible, custom window buttons work, and the window
remains usable at its minimum size.
Native-window automation is deferred until it provides stable value.
