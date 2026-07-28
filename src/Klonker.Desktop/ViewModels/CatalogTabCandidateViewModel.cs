using CommunityToolkit.Mvvm.ComponentModel;

namespace Klonker.Desktop.ViewModels;

public sealed partial class CatalogTabCandidateViewModel : ViewModelBase
{
    public CatalogTabCandidateViewModel(
        string identity,
        string label,
        string detail)
    {
        Identity = identity;
        Label = label;
        Detail = detail;
    }

    public string Identity { get; }

    public string Label { get; }

    public string Detail { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
