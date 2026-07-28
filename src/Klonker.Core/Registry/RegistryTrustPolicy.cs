using System.Collections.Immutable;

namespace Klonker.Core.Registry;

public sealed record RegistryTrustPolicy(
    string PublisherId,
    ImmutableArray<RegistryTrustedKey> Keys,
    bool RequireSignature = true);
