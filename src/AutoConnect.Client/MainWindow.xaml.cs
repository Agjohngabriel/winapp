using AutoConnect.Client.ViewModels;
using System.Windows;

namespace AutoConnect.Client;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}