using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Registry;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TrustedPublisherKeyViewModel : ViewModelBase
{
    private readonly string algorithm =
        RegistrySignatureVerifier.RsaPkcs1Sha256;

    public TrustedPublisherKeyViewModel()
    {
    }

    public TrustedPublisherKeyViewModel(RegistryTrustedKey key)
    {
        KeyId = key.KeyId;
        PublicKeySpki = key.PublicKeySpki;
        IsRevoked = key.Revoked;
    }

    public string Algorithm => algorithm;

    [ObservableProperty]
    public partial string KeyId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PublicKeySpki { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRevoked { get; set; }

    public RegistryTrustedKey ToModel() =>
        new(KeyId.Trim(), Algorithm, PublicKeySpki.Trim(), IsRevoked);
}
