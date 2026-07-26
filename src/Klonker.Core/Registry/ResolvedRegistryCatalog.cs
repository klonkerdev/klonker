using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record ResolvedRegistryCatalog(
    ImmutableArray<RegistryTemplatePackage> Templates);
