using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoConnect.Client.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "Dashboard";
}