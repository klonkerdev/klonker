using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Core.Registry;

namespace Klonker.Desktop.ViewModels;

public sealed partial class RegistrySourceEditorViewModel : ViewModelBase
{
    public static IReadOnlyList<string> KindOptions { get; } =
        ["Remote", "Local"];

    public RegistrySourceEditorViewModel()
    {
    }

    public RegistrySourceEditorViewModel(RegistrySource source)
    {
        Name = source.Name;
        Kind = source.Kind == RegistrySourceKind.Remote ? "Remote" : "Local";
        Location = source.Location;
        IsEnabled = source.Enabled;
        RequireSignature = source.TrustPolicy?.RequireSignature ?? false;
        PublisherId = source.TrustPolicy?.PublisherId ?? string.Empty;
        if (source.TrustPolicy is not null)
        {
            foreach (var key in source.TrustPolicy.Keys)
            {
                TrustedKeys.Add(new TrustedPublisherKeyViewModel(key));
            }
        }
    }

    public ObservableCollection<TrustedPublisherKeyViewModel> TrustedKeys { get; } = [];

    public bool IsRemote => Kind == "Remote";

    [ObservableProperty]
    public partial string Name { get; set; } = "New registry";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemote))]
    public partial string Kind { get; set; } = "Remote";

    [ObservableProperty]
    public partial string Location { get; set; } = "https://";

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool RequireSignature { get; set; }

    [ObservableProperty]
    public partial string PublisherId { get; set; } = string.Empty;

    public RegistrySource ToModel()
    {
        var kind = Kind == "Local"
            ? RegistrySourceKind.Local
            : RegistrySourceKind.Remote;
        RegistryTrustPolicy? trustPolicy = null;
        if (IsRemote &&
            (RequireSignature ||
             !string.IsNullOrWhiteSpace(PublisherId) ||
             TrustedKeys.Count > 0))
        {
            trustPolicy = new RegistryTrustPolicy(
                PublisherId.Trim(),
                TrustedKeys
                    .Select(key => key.ToModel())
                    .ToImmutableArray(),
                RequireSignature);
        }

        return new RegistrySource(
            Name.Trim(),
            kind,
            Location.Trim(),
            IsEnabled,
            trustPolicy);
    }
}
