using System.Collections.Immutable;
using Klonker.Core.Templates;

namespace Klonker.Core.Modules;

public sealed record ModuleManifest(
    int SchemaVersion,
    string Id,
    string Name,
    string Description,
    string Version,
    string Language,
    string SourceLicense,
    ImmutableArray<string> Tags,
    ImmutableArray<ModuleSlotDefinition> Slots,
    ImmutableArray<TemplateParameterDefinition> Parameters,
    ImmutableArray<ModuleDependency> Dependencies,
    string? PostGenerationInstructions);
