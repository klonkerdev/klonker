using System.Collections.Immutable;

namespace Klonker.Core.Templates;

public sealed record TemplateParameterDefinition(
    string Id,
    TemplateParameterType Type,
    string Label,
    string? Description,
    bool Required,
    object? DefaultValue,
    string? Validation,
    ImmutableArray<string> Values);
