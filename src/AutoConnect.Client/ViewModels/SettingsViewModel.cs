using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoConnect.Client.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "Settings";
}