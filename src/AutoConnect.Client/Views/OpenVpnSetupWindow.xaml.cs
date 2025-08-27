// src/AutoConnect.Client/Views/OpenVpnSetupWindow.xaml.cs
using AutoConnect.Client.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace AutoConnect.Client.Views;

public partial class OpenVpnSetupWindow : Window
{
    private readonly ILogger<OpenVpnSetupWindow> _logger;
    private readonly IConfiguration _configuration;
    private readonly OpenVpnConfigHelper _configHelper;
    private string _targetConfigPath;
    private bool _configurationValid = false;

    public bool ConfigurationSaved { get; private set; } = false;

    public OpenVpnSetupWindow(
        ILogger<OpenVpnSetupWindow> logger,
        IConfiguration configuration,
        OpenVpnConfigHelper configHelper)
    {
        InitializeComponent();
        
        _logger = logger;
        _configuration = configuration;
        _configHelper = configHelper;

        var configDir = _configuration["VpnSettings:ConfigPath"] ?? 
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn-configs");
        _targetConfigPath = Path.Combine(configDir, "client.ovpn");

        _logger.LogInformation("OpenVPN Setup window initialized. Target config path: {ConfigPath}", _targetConfigPath);
    }

    private void BrowseConfigFile_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select OpenVPN Configuration File",
            Filter = "OpenVPN Config Files (*.ovpn)|*.ovpn|All Files (*.*)|*.*",
            DefaultExt = ".ovpn"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            ConfigFilePathTextBox.Text = openFileDialog.FileName;
            _ = ValidateImportedConfigAsync(openFileDialog.FileName);
        }
    }

    private async Task ValidateImportedConfigAsync(string configPath)
    {
        try
        {
            ValidationPanel.Visibility = Visibility.Visible;
            ValidationResultsTextBlock.Text = "Validating configuration...";
            ImportConfigButton.IsEnabled = false;

            var validation = await _configHelper.ValidateConfigAsync(configPath);

            var results = new List<string>();
            
            if (validation.IsValid)
            {
                results.Add("✅ Configuration is valid and ready to use");
                _configurationValid = true;
                ImportConfigButton.IsEnabled = true;
                SaveButton.IsEnabled = true;
            }
            else
            {
                results.Add("❌ Configuration has errors:");
                foreach (var error in validation.Errors)
                {
                    results.Add($"   • {error}");
                }
                _configurationValid = false;
            }

            if (validation.Warnings.Any())
            {
                results.Add("\n⚠️ Warnings:");
                foreach (var warning in validation.Warnings)
                {
                    results.Add($"   • {warning}");
                }
            }

            if (validation.Info.Any())
            {
                results.Add("\nℹ️ Recommendations:");
                foreach (var info in validation.Info)
                {
                    results.Add($"   • {info}");
                }
            }

            ValidationResultsTextBlock.Text = string.Join("\n", results);
            _logger.LogInformation("Config validation completed: Valid={IsValid}, Errors={ErrorCount}", 
                validation.IsValid, validation.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating configuration");
            ValidationResultsTextBlock.Text = $"❌ Error validating configuration: {ex.Message}";
            _configurationValid = false;
            ImportConfigButton.IsEnabled = false;
        }
    }

    private async void ImportConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sourcePath = ConfigFilePathTextBox.Text;
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
            {
                MessageBox.Show("Please select a valid configuration file.", "Invalid File", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ImportConfigButton.IsEnabled = false;
            ImportConfigButton.Content = "Importing...";

            var success = await _configHelper.ImportConfigFileAsync(sourcePath, _targetConfigPath);

            if (success)
            {
                MessageBox.Show($"Configuration imported successfully!\n\nSaved to: {_targetConfigPath}", 
                    "Import Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                
                ConfigurationSaved = true;
                SaveButton.IsEnabled = true;
                _logger.LogInformation("Successfully imported OpenVPN config from {Source}", sourcePath);
            }
            else
            {
                MessageBox.Show("Failed to import configuration. Check the logs for details.", 
                    "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing configuration");
            MessageBox.Show($"Error importing configuration: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportConfigButton.IsEnabled = _configurationValid;
            ImportConfigButton.Content = "Import Configuration";
        }
    }

    private void BrowseCaCert_Click(object sender, RoutedEventArgs e)
    {
        BrowseCertificateFile("Select CA Certificate", CaCertPathTextBox);
    }

    private void BrowseClientCert_Click(object sender, RoutedEventArgs e)
    {
        BrowseCertificateFile("Select Client Certificate", ClientCertPathTextBox);
    }

    private void BrowseClientKey_Click(object sender, RoutedEventArgs e)
    {
        BrowseCertificateFile("Select Client Private Key", ClientKeyPathTextBox);
    }

    private void BrowseCertificateFile(string title, TextBox targetTextBox)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Certificate Files (*.crt;*.pem;*.key)|*.crt;*.pem;*.key|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            targetTextBox.Text = openFileDialog.FileName;
        }
    }

    private async void CreateConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate required fields
            if (string.IsNullOrWhiteSpace(ServerAddressTextBox.Text))
            {
                MessageBox.Show("Please enter a server address.", "Missing Information", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(ServerPortTextBox.Text, out var port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Please enter a valid port number (1-65535).", "Invalid Port", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check authentication method
            if (CertificateAuthRadio.IsChecked == true)
            {
                if (string.IsNullOrWhiteSpace(CaCertPathTextBox.Text) ||
                    string.IsNullOrWhiteSpace(ClientCertPathTextBox.Text) ||
                    string.IsNullOrWhiteSpace(ClientKeyPathTextBox.Text))
                {
                    MessageBox.Show("Please select all required certificate files (CA, Client Certificate, and Client Key).", 
                        "Missing Certificates", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Verify certificate files exist
                var certFiles = new[] { CaCertPathTextBox.Text, ClientCertPathTextBox.Text, ClientKeyPathTextBox.Text };
                foreach (var certFile in certFiles)
                {
                    if (!File.Exists(certFile))
                    {
                        MessageBox.Show($"Certificate file not found: {certFile}", 
                            "Missing File", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }

            CreateConfigButton.IsEnabled = false;
            CreateConfigButton.Content = "Creating...";

            // Build configuration input
            var configInput = new OpenVpnConfigInput
            {
                ServerAddress = ServerAddressTextBox.Text.Trim(),
                ServerPort = port,
                Protocol = ProtocolComboBox.SelectedIndex == 0 ? "udp" : "tcp",
                UseCompression = CompressionCheckBox.IsChecked == true,
                RedirectGateway = RedirectGatewayCheckBox.IsChecked == true,
                UseUsernamePassword = UsernamePasswordAuthRadio.IsChecked == true
            };

            // Handle certificate files
            if (CertificateAuthRadio.IsChecked == true)
            {
                // Read certificate files and embed them inline
                try
                {
                    configInput.HasInlineCertificates = true;
                    configInput.CaCertificate = await File.ReadAllTextAsync(CaCertPathTextBox.Text);
                    configInput.ClientCertificate = await File.ReadAllTextAsync(ClientCertPathTextBox.Text);
                    configInput.ClientKey = await File.ReadAllTextAsync(ClientKeyPathTextBox.Text);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading certificate files");
                    MessageBox.Show($"Error reading certificate files: {ex.Message}", 
                        "Certificate Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Create the configuration
            var success = await _configHelper.CreateConfigFromInputAsync(_targetConfigPath, configInput);

            if (success)
            {
                MessageBox.Show($"Configuration created successfully!\n\nSaved to: {_targetConfigPath}", 
                    "Configuration Created", MessageBoxButton.OK, MessageBoxImage.Information);
                
                ConfigurationSaved = true;
                SaveButton.IsEnabled = true;
                _logger.LogInformation("Successfully created OpenVPN config at {ConfigPath}", _targetConfigPath);
            }
            else
            {
                MessageBox.Show("Failed to create configuration. Check the logs for details.", 
                    "Creation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating configuration");
            MessageBox.Show($"Error creating configuration: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CreateConfigButton.IsEnabled = true;
            CreateConfigButton.Content = "Create Configuration";
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_targetConfigPath))
            {
                MessageBox.Show("No configuration file found. Please import or create a configuration first.", 
                    "No Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TestConnectionButton.IsEnabled = false;
            TestConnectionButton.Content = "Testing...";
            TestResultsPanel.Visibility = Visibility.Visible;
            TestResultsTextBlock.Text = "Starting connection test...\n";

            // Validate configuration first
            var validation = await _configHelper.ValidateConfigAsync(_targetConfigPath);
            
            AppendTestResult($"Configuration validation: {(validation.IsValid ? "PASSED" : "FAILED")}");
            
            if (!validation.IsValid)
            {
                foreach (var error in validation.Errors)
                {
                    AppendTestResult($"  ERROR: {error}");
                }
                return;
            }

            if (validation.Warnings.Any())
            {
                foreach (var warning in validation.Warnings)
                {
                    AppendTestResult($"  WARNING: {warning}");
                }
            }

            AppendTestResult("\nTesting OpenVPN executable...");
            
            // Test if OpenVPN is available
            var openVpnPath = FindOpenVpnExecutable();
            if (string.IsNullOrEmpty(openVpnPath))
            {
                AppendTestResult("ERROR: OpenVPN executable not found. Please install OpenVPN.");
                return;
            }
            
            AppendTestResult($"OpenVPN found at: {openVpnPath}");

            AppendTestResult("\nAttempting test connection (15 seconds timeout)...");

            // Attempt a brief connection test
            var testResult = await PerformConnectionTestAsync(openVpnPath);
            
            if (testResult.Success)
            {
                AppendTestResult("SUCCESS: Connection test completed successfully!");
                AppendTestResult($"Connection established in {testResult.ConnectionTime:F1} seconds");
                if (!string.IsNullOrEmpty(testResult.AssignedIp))
                {
                    AppendTestResult($"Assigned VPN IP: {testResult.AssignedIp}");
                }
            }
            else
            {
                AppendTestResult($"FAILED: {testResult.ErrorMessage}");
                if (!string.IsNullOrEmpty(testResult.LogOutput))
                {
                    AppendTestResult("\nDetailed log output:");
                    AppendTestResult(testResult.LogOutput);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during connection test");
            AppendTestResult($"ERROR: Test failed with exception: {ex.Message}");
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            TestConnectionButton.Content = "Start Connection Test";
        }
    }

    private string FindOpenVpnExecutable()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\OpenVPN\bin\openvpn.exe",
            @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
            @"C:\OpenVPN\bin\openvpn.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Try system PATH
        try
        {
            var testProcess = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "openvpn",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            
            testProcess.Start();
            testProcess.WaitForExit(3000);
            
            if (testProcess.ExitCode == 0 || testProcess.ExitCode == 1)
            {
                return "openvpn";
            }
        }
        catch
        {
            // Ignore
        }

        return string.Empty;
    }

    private async Task<ConnectionTestResult> PerformConnectionTestAsync(string openVpnPath)
    {
        var result = new ConnectionTestResult();
        var logOutput = new List<string>();
        var startTime = DateTime.Now;

        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = openVpnPath,
                    Arguments = $"--config \"{_targetConfigPath}\" --verb 3",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logOutput.Add(e.Data);
                    
                    // Look for success indicators
                    if (e.Data.Contains("Initialization Sequence Completed") ||
                        e.Data.Contains("CONNECTED,SUCCESS"))
                    {
                        result.Success = true;
                        result.ConnectionTime = (DateTime.Now - startTime).TotalSeconds;
                        
                        // Try to extract assigned IP
                        var ipMatch = System.Text.RegularExpressions.Regex.Match(e.Data, @"(\d+\.\d+\.\d+\.\d+)");
                        if (ipMatch.Success)
                        {
                            result.AssignedIp = ipMatch.Groups[1].Value;
                        }
                    }
                    
                    // Look for error indicators
                    if (e.Data.Contains("AUTH_FAILED") || 
                        e.Data.Contains("TLS Error") ||
                        e.Data.Contains("Connection refused"))
                    {
                        result.ErrorMessage = e.Data;
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logOutput.Add($"ERROR: {e.Data}");
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait up to 15 seconds for connection
            var timeout = TimeSpan.FromSeconds(15);
            var checkInterval = 500; // ms

            while ((DateTime.Now - startTime) < timeout && !process.HasExited)
            {
                await Task.Delay(checkInterval);
                
                if (result.Success)
                {
                    // Connection successful, terminate test
                    break;
                }
            }

            // Clean up process
            if (!process.HasExited)
            {
                process.Kill();
                await Task.Delay(1000); // Give it time to clean up
            }

            if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            {
                if (process.HasExited && process.ExitCode != 0)
                {
                    result.ErrorMessage = $"OpenVPN process exited with code {process.ExitCode}";
                }
                else
                {
                    result.ErrorMessage = "Connection test timed out after 15 seconds";
                }
            }

            result.LogOutput = string.Join("\n", logOutput.TakeLast(20)); // Last 20 lines
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            result.LogOutput = string.Join("\n", logOutput);
        }

        return result;
    }

    private void AppendTestResult(string message)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            TestResultsTextBlock.Text += message + "\n";
        });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigurationSaved)
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Please import or create a configuration first.", 
                "No Configuration", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = "";
        public string LogOutput { get; set; } = "";
        public double ConnectionTime { get; set; }
        public string AssignedIp { get; set; } = "";
    }
}