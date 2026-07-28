using System.Collections.Immutable;
using Klonker.Core.Templates;

namespace Klonker.Core.Authoring;

public sealed record TemplateAuthoringRequest(
    string DestinationPath,
    string? ExistingContentPath,
    string NamespaceId,
    string PackageId,
    string Name,
    string Description,
    string Version,
    string Language,
    ImmutableArray<string> BuildSystems,
    ImmutableArray<string> Platforms,
    string SourceLicense,
    string LicenseSummary,
    bool CreateReadme,
    ImmutableArray<TemplateAuthoringSeedFile> SeedFiles,
    ImmutableArray<TemplateParameterDefinition> Parameters = default,
    ImmutableArray<TemplatePrerequisite> Prerequisites = default,
    ImmutableArray<string> Tags = default);
