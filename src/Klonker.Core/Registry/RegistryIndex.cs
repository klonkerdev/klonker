using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryIndex(
    int SchemaVersion,
    string RegistryId,
    string DisplayName,
    ImmutableArray<RegistryTemplateEntry> Templates);
