using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryModuleVersionSelectionResult(
    ImmutableArray<RegistryModuleVersionSelection> Selections);
