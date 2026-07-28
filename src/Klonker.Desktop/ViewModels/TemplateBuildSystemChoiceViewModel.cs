using CommunityToolkit.Mvvm.ComponentModel;
using Klonker.Desktop.Services;

namespace Klonker.Desktop.ViewModels;

public sealed partial class TemplateBuildSystemChoiceViewModel : ViewModelBase
{
    public TemplateBuildSystemChoiceViewModel(TemplateBuildSystemOption option)
    {
        Option = option;
    }

    public TemplateBuildSystemOption Option { get; }

    public string Id => Option.Id;

    public string Name => Option.Name;

    public string Description => Option.Description;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
