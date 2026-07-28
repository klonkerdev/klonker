namespace Klonker.Core.Registry;

public sealed record RegistryTrustedKey(
    string KeyId,
    string Algorithm,
    string PublicKeySpki,
    bool Revoked = false);
