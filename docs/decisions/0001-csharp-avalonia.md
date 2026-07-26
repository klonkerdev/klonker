# 0001: C# and Avalonia

**Status:** Accepted

## Context

Klonker needs a Windows-first desktop UI, testable cross-platform domain code,
and a maintainable path to a future CLI.

## Decision

Use C# on .NET 10. Put domain behavior in an Avalonia-free Core library and use
Avalonia AXAML with CommunityToolkit.Mvvm for Desktop. Do not use ReactiveUI.

## Consequences

Core can be tested and reused without a windowing runtime. Desktop remains
portable in principle while Windows behavior is prioritized. The repository
must maintain a strict Desktop-to-Core dependency direction.
