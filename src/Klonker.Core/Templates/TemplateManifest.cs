using System.Collections.Immutable;

namespace Klonker.Core.Templates;

public sealed record TemplateManifest(
    int SchemaVersion,
    string Id,
    string FamilyId,
    string VariantId,
    string Name,
    string Description,
    string Version,
    string TargetOs,
    string BuildSystem,
    string SourceLicense,
    ImmutableArray<TemplateParameterDefinition> Parameters,
    string? Logo = null,
    ImmutableArray<string> Tags = default,
    bool IsFavorite = false,
    ImmutableArray<TemplatePrerequisite> Prerequisites = default);
