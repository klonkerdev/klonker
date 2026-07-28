namespace Klonker.Core.Registry;

public sealed record VerifiedRegistrySignature(
    string PublisherId,
    string KeyId,
    string Algorithm,
    string IndexSha256);
