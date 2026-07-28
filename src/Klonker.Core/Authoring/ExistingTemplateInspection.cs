using System.Collections.Immutable;
using Klonker.Core.Diagnostics;

namespace Klonker.Core.Authoring;

public sealed record ExistingTemplateInspection(
    string RootPath,
    ExistingTemplateKind Kind,
    string Summary,
    string ContentSourcePath,
    ExistingTemplateMetadata? Metadata,
    ImmutableArray<string> Files,
    ImmutableArray<ValidationIssue> Issues)
{
    public bool HasErrors =>
        Issues.Any(issue => issue.Severity == ValidationSeverity.Error);

    public bool IsAlreadyRegistrySource =>
        Kind == ExistingTemplateKind.RegistrySourcePackage;
}
