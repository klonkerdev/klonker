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
  fallback, detached publisher signatures, active/revoked trust keys, and
  zero-network offline reuse.
- **Version policy:** multi-version identities, stable/prerelease ordering,
  exact pins, unavailable-pin warnings, and deterministic fallbacks.
- **Module integration:** module manifest/slot validation, restricted
  rendering, dependency-license aggregation, non-empty destinations,
  complete conflict preflight, rollback, and read-back verification.
- **WSL boundary:** UTF-16/UTF-8 distribution output decoding, absolute Linux
  path mapping, and unsafe/incompatible path rejection without requiring an
  installed distribution.
- **Desktop logic:** the single test project references Desktop and exercises
  catalog failure, registry-qualified package grouping, package/variant
  navigation, filters, platform/build-system presentation, tree construction,
  independent folder expansion, semantic file-icon classification, tree
  selection, manifest presentation metadata, syntax tokenization, app-local
  favorite persistence, settings,
  language presentation, no-build-system variants, custom-tag
  filtering/color assignment, strict copied C++/Lua source decoding, and
  preview behavior, destination picking, explicit generation confirmation,
  structured diagnostics, consented prerequisite probes, and preview navigation without
  launching Avalonia windows.
- **Template authoring:** temporary source trees validate platform/build-system
  matrix planning, build-seed isolation, `any` exclusivity, starter rendering,
  tool-directory exclusion, source/destination
  separation, runtime/source inspection, actionable schema findings,
  transactional installation, and headless wizard navigation.
- **Registry authoring:** temporary source workspaces validate generic
  package/variant and module discovery, imported package layout, runtime composition,
  canonical integrity metadata, transactional `dist` installation, detached
  signing, and reload through the production local-registry reader.

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
expected sample CMake and C++ content. Lua preview tests cover copied-source
decoding, code-tree classification, and host-owned syntax tokenization.
Executor tests assert no overwrite and
that no output escapes the destination. Registry archive tests must include
traversal, case collisions, file/directory collisions, links where practical,
checksum mismatch, and offline cache misses.

Repository validation enumerates sample registry indexes, their template and
module entries, and all referenced manifests. A new data-only package,
variant, or module must pass
the same generic identity/path/integrity checks and does not require a
hardcoded test branch.

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
remains usable at its minimum size. Open Settings and smoke-test registry row
editing, theme switching, publisher key rows, probe consent, and the scoped
cache/preferences reset actions.
Open the Template wizard and smoke-test all three entry modes, forward/back
animation, language/build-system mapping, multi-build and `Any platform`
cards, catalog-template copying, existing-folder
refresh, issue readability, preview selection, and successful generation into
a new directory. Confirm the inspected source remains byte-for-byte unchanged.
Open the Registry workspace wizard and smoke-test a development registry with
an imported authoring package, local registration, a repeat rebuild, existing
source validation, and production key generation with the private key outside
the workspace. Confirm the built variants appear after catalog refresh.
Native-window automation is deferred until it provides stable value.

For modules, switch to the Modules tab, verify slot and parameter rendering,
preview the complete tree, target a non-empty folder with unrelated content,
and confirm that a colliding path blocks all writes. Review aggregated
licenses and rendered post-generation instructions. Create favorite and
curated personal tabs with `+`, restart, and verify they remain app-local.
For WSL, start a test distribution, refresh the running-only list, generate to
an absolute Linux path, and verify Klonker reports directory and byte
read-back success.
