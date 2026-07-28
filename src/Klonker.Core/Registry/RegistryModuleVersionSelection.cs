using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryModuleVersionSelection(
    string QualifiedModuleId,
    RegistryModulePackage Selected,
    ImmutableArray<RegistryModulePackage> Candidates,
    string Reason);
