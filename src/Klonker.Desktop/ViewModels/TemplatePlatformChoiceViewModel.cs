using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TemplatePlatformChoiceViewModel : ViewModelBase
{
    public TemplatePlatformChoiceViewModel(TemplatePlatformOption option)
    {
        Option = option;
    }

    public TemplatePlatformOption Option { get; }

    public string Id => Option.Id;

    public string Name => Option.Name;

    public string Description => Option.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
