// src/AutoConnect.Client/Helpers/OpenVpnConfigHelper.cs
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;

namespace AutoConnect.Client.Helpers;

public class OpenVpnConfigHelper
{
    private readonly ILogger<OpenVpnConfigHelper> _logger;

    public OpenVpnConfigHelper(ILogger<OpenVpnConfigHelper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates an OpenVPN configuration file
    /// </summary>
    public async Task<ValidationResult> ValidateConfigAsync(string configPath)
    {
        var result = new ValidationResult();

        try
        {
            if (!File.Exists(configPath))
            {
                result.AddError($"Configuration file not found: {configPath}");
                return result;
            }

            var content = await File.ReadAllTextAsync(configPath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Check for required client directive
            if (!HasDirective(lines, "client"))
            {
                result.AddError("Missing 'client' directive - this should be a client configuration");
            }

            // Check for remote server
            if (!HasDirective(lines, "remote"))
            {
                result.AddError("Missing 'remote' directive - no VPN server specified");
            }

            // Check for authentication method
            var hasInlineCerts = content.Contains("<ca>") && (content.Contains("<cert>") || content.Contains("<key>"));
            var hasCertFiles = HasDirective(lines, "ca") && (HasDirective(lines, "cert") || HasDirective(lines, "key"));
            var hasAuthUserPass = HasDirective(lines, "auth-user-pass");

            if (!hasInlineCerts && !hasCertFiles && !hasAuthUserPass)
            {
                result.AddError("Missing authentication method - no certificates or username/password authentication found");
            }

            // Check device type
            if (!HasDirective(lines, "dev"))
            {
                result.AddWarning("Missing 'dev' directive - defaulting to 'tun'");
            }

            // Check protocol
            if (!HasDirective(lines, "proto"))
            {
                result.AddWarning("Missing 'proto' directive - defaulting to 'udp'");
            }

            // Validate remote server format
            var remoteLines = GetDirectiveValues(lines, "remote");
            foreach (var remoteLine in remoteLines)
            {
                if (!ValidateRemoteDirective(remoteLine))
                {
                    result.AddWarning($"Remote server format may be incorrect: {remoteLine}");
                }
            }

            // Check for potential issues
            CheckForCommonIssues(lines, result);

            result.IsValid = !result.Errors.Any();
            _logger.LogInformation("Config validation completed: {ErrorCount} errors, {WarningCount} warnings",
                result.Errors.Count, result.Warnings.Count);

        }
        catch (Exception ex)
        {
            result.AddError($"Error reading configuration file: {ex.Message}");
            _logger.LogError(ex, "Error validating OpenVPN config");
        }

        return result;
    }

    /// <summary>
    /// Creates a basic OpenVPN configuration from user input
    /// </summary>
    public async Task<bool> CreateConfigFromInputAsync(string configPath, OpenVpnConfigInput input)
    {
        try
        {
            var configDir = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            var config = new StringBuilder();

            // Basic client configuration
            config.AppendLine("# AutoConnect OpenVPN Client Configuration");
            config.AppendLine($"# Generated on {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            config.AppendLine();
            config.AppendLine("client");
            config.AppendLine($"dev {input.DeviceType}");
            config.AppendLine($"proto {input.Protocol}");
            config.AppendLine($"remote {input.ServerAddress} {input.ServerPort}");
            config.AppendLine();

            // Connection settings
            config.AppendLine("resolv-retry infinite");
            config.AppendLine("nobind");
            config.AppendLine("persist-key");
            config.AppendLine("persist-tun");
            config.AppendLine();

            // Authentication
            if (input.HasInlineCertificates)
            {
                config.AppendLine("<ca>");
                config.AppendLine(input.CaCertificate);
                config.AppendLine("</ca>");
                config.AppendLine();

                config.AppendLine("<cert>");
                config.AppendLine(input.ClientCertificate);
                config.AppendLine("</cert>");
                config.AppendLine();

                config.AppendLine("<key>");
                config.AppendLine(input.ClientKey);
                config.AppendLine("</key>");
                config.AppendLine();
            }
            else if (input.UseUsernamePassword)
            {
                config.AppendLine("auth-user-pass");
            }
            else
            {
                config.AppendLine("ca ca.crt");
                config.AppendLine("cert client.crt");
                config.AppendLine("key client.key");
            }

            // Security settings
            config.AppendLine("remote-cert-tls server");
            config.AppendLine($"cipher {input.Cipher}");
            config.AppendLine($"auth {input.Auth}");
            config.AppendLine();

            // Compression
            if (input.UseCompression)
            {
                config.AppendLine("comp-lzo");
            }

            // Routing
            if (input.RedirectGateway)
            {
                config.AppendLine("redirect-gateway def1");
            }

            // DNS
            if (input.CustomDns?.Any() == true)
            {
                foreach (var dns in input.CustomDns)
                {
                    config.AppendLine($"dhcp-option DNS {dns}");
                }
            }

            // Connection maintenance
            config.AppendLine("keepalive 10 60");
            config.AppendLine();

            // Logging
            config.AppendLine("verb 3");
            config.AppendLine("mute 20");

            await File.WriteAllTextAsync(configPath, config.ToString());
            _logger.LogInformation("Created OpenVPN config file: {ConfigPath}", configPath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating OpenVPN config file");
            return false;
        }
    }

    /// <summary>
    /// Imports an existing .ovpn file and validates it
    /// </summary>
    public async Task<bool> ImportConfigFileAsync(string sourcePath, string destinationPath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                _logger.LogError("Source config file not found: {SourcePath}", sourcePath);
                return false;
            }

            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy the file
            await File.WriteAllTextAsync(destinationPath, await File.ReadAllTextAsync(sourcePath));

            // Validate the imported config
            var validation = await ValidateConfigAsync(destinationPath);

            if (validation.IsValid)
            {
                _logger.LogInformation("Successfully imported OpenVPN config from {SourcePath} to {DestPath}",
                    sourcePath, destinationPath);
            }
            else
            {
                _logger.LogWarning("Imported config has validation issues: {Errors}",
                    string.Join(", ", validation.Errors));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing OpenVPN config file");
            return false;
        }
    }

    private bool HasDirective(string[] lines, string directive)
    {
        return lines.Any(line => line.Trim().StartsWith(directive + " ") || line.Trim() == directive);
    }

    private IEnumerable<string> GetDirectiveValues(string[] lines, string directive)
    {
        return lines
            .Where(line => line.Trim().StartsWith(directive + " "))
            .Select(line => line.Trim().Substring(directive.Length + 1).Trim());
    }

    private bool ValidateRemoteDirective(string remoteLine)
    {
        var parts = remoteLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Should have at least server address, optionally port and protocol
        if (parts.Length < 1 || parts.Length > 3)
            return false;

        // Basic hostname/IP validation
        var serverAddress = parts[0];
        if (string.IsNullOrWhiteSpace(serverAddress))
            return false;

        // If port is specified, validate it
        if (parts.Length >= 2)
        {
            if (!int.TryParse(parts[1], out var port) || port < 1 || port > 65535)
                return false;
        }

        // If protocol is specified, validate it
        if (parts.Length >= 3)
        {
            var protocol = parts[2].ToLower();
            if (protocol != "udp" && protocol != "tcp")
                return false;
        }

        return true;
    }

    private void CheckForCommonIssues(string[] lines, ValidationResult result)
    {
        // Check for deprecated directives
        var deprecatedDirectives = new[] { "tls-remote", "ns-cert-type" };
        foreach (var deprecated in deprecatedDirectives)
        {
            if (HasDirective(lines, deprecated))
            {
                result.AddWarning($"Deprecated directive found: {deprecated}");
            }
        }

        // Check for potential Windows-specific issues
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            if (!HasDirective(lines, "route-method"))
            {
                result.AddInfo("Consider adding 'route-method exe' for better Windows compatibility");
            }
        }

        // Check for management interface conflicts
        if (HasDirective(lines, "management"))
        {
            result.AddWarning("Config contains management directive - this may conflict with AutoConnect's management interface");
        }

        // Check for pull directive
        if (!HasDirective(lines, "pull"))
        {
            result.AddInfo("Consider adding 'pull' directive to receive server configuration");
        }
    }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Info { get; } = new();

    public void AddError(string message) => Errors.Add(message);
    public void AddWarning(string message) => Warnings.Add(message);
    public void AddInfo(string message) => Info.Add(message);
}

public class OpenVpnConfigInput
{
    public string ServerAddress { get; set; } = "";
    public int ServerPort { get; set; } = 1194;
    public string Protocol { get; set; } = "udp";
    public string DeviceType { get; set; } = "tun";
    public string Cipher { get; set; } = "AES-256-CBC";
    public string Auth { get; set; } = "SHA256";
    public bool UseCompression { get; set; } = true;
    public bool RedirectGateway { get; set; } = true;
    public bool UseUsernamePassword { get; set; } = false;
    public bool HasInlineCertificates { get; set; } = false;
    public string CaCertificate { get; set; } = "";
    public string ClientCertificate { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public List<string> CustomDns { get; set; } = new() { "8.8.8.8", "8.8.4.4" };
}