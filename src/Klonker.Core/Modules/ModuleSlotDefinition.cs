namespace Klonker.Core.Modules;

public sealed record ModuleSlotDefinition(
    string Id,
    string Label,
    string Description,
    bool Required,
    string? DefaultPath);
