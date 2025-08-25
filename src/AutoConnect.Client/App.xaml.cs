using AutoConnect.Client.Configuration;
using AutoConnect.Client.Services;
using AutoConnect.Client.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace AutoConnect.Client;

public partial class App : Application
{
    public IServiceProvider ServiceProvider { get; private set; } = null!;
    public IConfiguration Configuration { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Configure global exception handling
            SetupExceptionHandling();

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true);

            Configuration = builder.Build();

            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);

            ServiceProvider = serviceCollection.BuildServiceProvider();

            // Initialize error handling
            var errorHandler = ServiceProvider.GetRequiredService<IErrorHandlingService>();
            var notificationService = ServiceProvider.GetRequiredService<INotificationService>();

            // Connect error handling to notification service
            if (errorHandler is ErrorHandlingService concreteErrorHandler)
            {
                concreteErrorHandler.NotificationRequested += async (sender, args) =>
                {
                    await notificationService.ShowNotificationAsync(args.Title, args.Message, args.Type);
                };
            }

            // Log application startup
            var logger = ServiceProvider.GetRequiredService<ILogger<App>>();
            logger.LogInformation("AutoConnect application starting up...");

            // Set the main window
            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            logger.LogInformation("AutoConnect application started successfully");
        }
        catch (Exception ex)
        {
            // Handle startup errors
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "startup-error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Startup Error: {ex}");

            MessageBox.Show($"Failed to start AutoConnect application:\n\n{ex.Message}\n\nCheck the logs for more details.",
                "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);

            Environment.Exit(1);
        }

        base.OnStartup(e);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configuration
        services.AddSingleton<IConfiguration>(Configuration);

        // Enhanced Logging
        services.AddEnhancedLogging(Configuration);

        // Error Handling & Notifications
        services.AddSingleton<IErrorHandlingService, ErrorHandlingService>();
        services.AddSingleton<INotificationService, NotificationService>();

        // HTTP Client with basic configuration
        services.AddHttpClient<IApiService, ApiService>(client =>
        {
            var baseUrl = Configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5029";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(Configuration.GetValue<int>("ApiSettings:Timeout", 30));
        });

        // Services - Use existing implementations for now
        services.AddSingleton<IApiService, ApiService>();
        services.AddSingleton<IVehicleService, ObdVehicleService>();
        services.AddSingleton<IVpnService, ProcessVpnService>();
        services.AddSingleton<Services.INavigationService, Services.NavigationService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    private void SetupExceptionHandling()
    {
        // Handle unhandled exceptions on the UI thread
        DispatcherUnhandledException += (sender, args) =>
        {
            HandleUnhandledException(args.Exception, "UI Thread Exception");
            args.Handled = true; // Prevent application crash
        };

        // Handle unhandled exceptions on background threads
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            HandleUnhandledException((Exception)args.ExceptionObject, "Background Thread Exception");
        };

        // Handle unhandled exceptions in tasks
        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            HandleUnhandledException(args.Exception, "Task Exception");
            args.SetObserved(); // Prevent application crash
        };
    }

    private void HandleUnhandledException(Exception exception, string context)
    {
        try
        {
            // Log to file immediately
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var crashInfo = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}:\n{exception}\n\n";
            File.AppendAllText(logPath, crashInfo);

            // Try to use error handling service if available
            if (ServiceProvider != null)
            {
                try
                {
                    var errorHandler = ServiceProvider.GetService<IErrorHandlingService>();
                    errorHandler?.HandleErrorAsync(context, exception, ErrorSeverity.Critical);
                }
                catch
                {
                    // If error handling fails, just continue
                }
            }

            // Show user-friendly error message
            var userMessage = "An unexpected error occurred. The application will continue running, but some features may not work properly.\n\n" +
                             "Error details have been logged for debugging purposes.";

            MessageBox.Show(userMessage, "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // Last resort - if everything fails, just show basic message
            MessageBox.Show("A critical error occurred. Please restart the application.",
                "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (ServiceProvider != null)
            {
                var logger = ServiceProvider.GetService<ILogger<App>>();
                logger?.LogInformation("AutoConnect application shutting down...");

                // Gracefully disconnect services
                var vehicleService = ServiceProvider.GetService<IVehicleService>();
                var vpnService = ServiceProvider.GetService<IVpnService>();

                Task.Run(async () =>
                {
                    try
                    {
                        if (vehicleService != null)
                            await vehicleService.DisconnectAsync();

                        if (vpnService != null)
                            await vpnService.DisconnectAsync();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Error during service shutdown");
                    }
                }).Wait(TimeSpan.FromSeconds(5)); // Wait max 5 seconds for cleanup

                logger?.LogInformation("AutoConnect application shutdown completed");

                if (ServiceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            // Log shutdown errors but don't prevent exit
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "shutdown-error.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Shutdown Error: {ex}");
            }
            catch { /* Ignore if we can't even log */ }
        }

        base.OnExit(e);
    }
}