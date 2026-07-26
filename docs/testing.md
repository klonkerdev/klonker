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
- **Desktop logic:** the single test project references Desktop and exercises
  catalog failure and preview behavior without launching Avalonia windows.

Run:

```powershell
dotnet test Klonker.slnx --configuration Debug
.\eng\validate.ps1
```

Tests do not require a network, C++ compiler, CMake, WSL, Git, or native UI.

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
that no output escapes the destination.

## UI policy

Prefer headless view-model tests for catalog/configuration/preview state.
Manual UI smoke testing is required after changes to AXAML, bindings, startup
catalog discovery, selection behavior, or theme/layout. Verify that the sample
appears, invalid configuration is readable, preview builds, selecting a text
file changes content, and the window remains usable at its minimum size.
Native-window automation is deferred until it provides stable value.
