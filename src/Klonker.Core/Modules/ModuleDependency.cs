namespace Klonker.Core.Modules;

public sealed record ModuleDependency(
    string Id,
    string Name,
    string Version,
    string License,
    string? ProjectUrl);
