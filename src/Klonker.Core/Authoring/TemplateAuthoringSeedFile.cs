namespace Klonker.Core.Authoring;

public sealed record TemplateAuthoringSeedFile(
    string RelativePath,
    string Content,
    bool VariantSpecific,
    string? BuildSystem = null);
