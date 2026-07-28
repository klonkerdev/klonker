namespace Klonker.Core.Registry;

public sealed record RegistrySigningKeyMaterial(
    string PublicKeySpki,
    string PrivateKeyPem);
