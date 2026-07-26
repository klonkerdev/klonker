# Roadmap

## Completed foundation

- [x] .NET 10 solution with Core, Avalonia Desktop, and xUnit projects
- [x] centralized packages, analyzers, formatting, scripts, and documentation
- [x] version-zero TOML manifest and local JSON registry reader
- [x] typed parameters, defaults, choices, and C++ identifier validation
- [x] restricted deterministic Scriban helpers
- [x] Windows path safety, reparse rejection, and immutable planning
- [x] transactional Core filesystem executor
- [x] development C++ CLI/CMake template
- [x] automated Core, executor, sample-output, and view-model coverage

## Current vertical slice

- [x] locate the checked-in development registry on desktop startup
- [x] show the sample template and generated configuration controls
- [x] build a Core generation plan
- [x] show the directory tree and selectable rendered text
- [x] present loading, validation, and rendering failures without crashing
- [ ] choose a destination and invoke the tested executor from Desktop

## Next milestones

- [ ] add a simple destination field/folder picker and explicit Generate action
- [ ] show generation confirmation/result and safe diagnostic details
- [ ] configure local registry locations outside the repository
- [ ] define package cache layout and offline behavior
- [ ] add checksums and registry-qualified template identity
- [ ] expand preview navigation and prerequisite messages

## Deferred

- [ ] network registry synchronization, signatures, and conflict policy
- [ ] generation inside a selected WSL distribution
- [ ] reusable modules/slots and dependency-license aggregation
- [ ] additional official template families in a separate repository
- [ ] plugins, MCP, or coding-agent integration
- [ ] any project build, execution, import, update, or management behavior
