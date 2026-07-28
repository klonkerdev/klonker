using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryTemplateVersionSelection(
    string QualifiedTemplateId,
    RegistryTemplatePackage Selected,
    ImmutableArray<RegistryTemplatePackage> Candidates,
    string Reason);
