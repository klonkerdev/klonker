namespace Klonker.Core.Registry;

public sealed record RegistryWorkspaceRequest(
    string DestinationPath,
    string RegistryId,
    string DisplayName,
    bool IsProduction,
    string? PublisherId,
    string? SigningKeyId,
    string? PublicKeySpki,
    string? InitialTemplatePath = null);
