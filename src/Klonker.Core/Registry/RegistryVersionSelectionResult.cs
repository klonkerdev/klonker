using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryVersionSelectionResult(
    ImmutableArray<RegistryTemplateVersionSelection> Selections);
