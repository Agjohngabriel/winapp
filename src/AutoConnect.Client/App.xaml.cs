using AutoConnect.Client.Services;
using AutoConnect.Client.ViewModels;
using AutoConnect.Client.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using System.Windows.Navigation;

namespace AutoConnect.Client;

public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    public IConfiguration Configuration { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

        Configuration = builder.Build();

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        ServiceProvider = serviceCollection.BuildServiceProvider();

        // Set the main window
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<IConfiguration>(Configuration);

        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });

        // HTTP Client
        services.AddHttpClient<IApiService, ApiService>(client =>
        {
            var baseUrl = Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5029";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Services
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IVehicleService, VehicleService>();
        services.AddSingleton<IVpnService, VpnService>();
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ServiceProvider?.Dispose();
        base.OnExit(e);
    }
}