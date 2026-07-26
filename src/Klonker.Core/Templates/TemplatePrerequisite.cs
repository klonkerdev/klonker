namespace Klonker.Core.Templates;

public sealed record TemplatePrerequisite(
    string Id,
    string Name,
    string Description,
    string RequiredFor);
