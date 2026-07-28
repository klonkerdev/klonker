using System.Collections.Immutable;

namespace Klonker.Core.Authoring;

public sealed record ExistingTemplateMetadata(
    string NamespaceId,
    string PackageId,
    string Name,
    string Description,
    string Version,
    string Language,
    ImmutableArray<string> BuildSystems,
    string SourceLicense,
    ImmutableArray<string> Platforms);
