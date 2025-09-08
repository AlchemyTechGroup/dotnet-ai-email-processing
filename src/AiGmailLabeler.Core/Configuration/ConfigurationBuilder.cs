using DotNetEnv;
using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;

namespace AiGmailLabeler.Core.Configuration;

public static class AppConfigurationBuilder
{
    public static IConfiguration Build(string? envFilePath = null)
    {
        var basePath = GetBasePath();

        // Load .env file if it exists (using DotNetEnv for compatibility)
        var envPath = envFilePath ?? Path.Combine(basePath, ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        // Build configuration with proper precedence: Environment variables override .env
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddInMemoryCollection(GetDefaultConfiguration())
            .AddEnvironmentVariables() // This will include both .env vars (loaded via Env.Load) and system env vars
            .Build();

        return configuration;
    }

    public static AppConfiguration BuildAndValidate(string? envFilePath = null)
    {
        var configuration = Build(envFilePath);
        var appConfig = new AppConfiguration();

        // Bind configuration with custom mapping for nested properties
        BindConfiguration(configuration, appConfig);

        // Validate the configuration
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(appConfig);

        if (!Validator.TryValidateObject(appConfig, validationContext, validationResults, true))
        {
            var errors = string.Join(Environment.NewLine, validationResults.Select(r => r.ErrorMessage));
            throw new InvalidOperationException($"Configuration validation failed:{Environment.NewLine}{errors}");
        }

        return appConfig;
    }

    private static void BindConfiguration(IConfiguration configuration, AppConfiguration appConfig)
    {
        // Bind main properties
        configuration.Bind(appConfig);

        // Manual binding for processor configurations with custom naming
        BindProcessorConfig(configuration, "PROCESSOR_Deduplicate_", appConfig.Processors.Deduplicate);
        BindProcessorConfig(configuration, "PROCESSOR_NoiseFilter_", appConfig.Processors.NoiseFilter);
        BindProcessorConfig(configuration, "PROCESSOR_PhishingClassifier_", appConfig.Processors.PhishingClassifier);
        BindProcessorConfig(configuration, "PROCESSOR_PhishingLabeler_", appConfig.Processors.PhishingLabeler);
        BindProcessorConfig(configuration, "PROCESSOR_TestKeyword_", appConfig.Processors.TestKeyword);
    }

    private static void BindProcessorConfig<T>(IConfiguration configuration, string prefix, T processorConfig) where T : class
    {
        var section = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(
                configuration.AsEnumerable()
                    .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(
                        kvp => kvp.Key.Substring(prefix.Length),
                        kvp => kvp.Value ?? string.Empty,
                        StringComparer.OrdinalIgnoreCase
                    )
            )
            .Build();

        section.Bind(processorConfig);
    }

    private static string GetBasePath()
    {
        // Start from current directory and walk up to find .env file or project root
        var currentDir = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".env")) ||
                File.Exists(Path.Combine(dir.FullName, "AiGmailLabeler.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return currentDir;
    }

    private static Dictionary<string, string> GetDefaultConfiguration()
    {
        return new Dictionary<string, string>
        {
            ["GoogleClientSecretsPath"] = "./secrets/client_secret_desktop.json",
            ["GmailTokenStoreDir"] = "./.tokens/gmail",
            ["OllamaBaseUrl"] = "http://192.168.4.23:11434",
            ["OllamaModel"] = "llama3.1:8b",
            ["PollIntervalSeconds"] = "20",
            ["MaxResults"] = "25",
            ["GmailQuery"] = "newer_than:2d -label:\"[AI]: Processed\"",
            ["AiPhishingLabel"] = "[AI]: Phishing Possible",
            ["AiProcessedLabel"] = "[AI]: Processed",
            ["ClassifyConfidenceThreshold"] = "0.6",
            ["HttpTimeoutSeconds"] = "30",
            ["BackoffMinSeconds"] = "5",
            ["BackoffMaxSeconds"] = "60",
            ["LogLevel"] = "Information",
            ["VerboseMode"] = "false",
            ["BodyMaxKb"] = "128",
            ["ProcessorOrder"] = "Deduplicate,TestKeyword,NoiseFilter,PhishingClassifier,PhishingLabeler",
            ["PROCESSOR_Deduplicate_Enabled"] = "true",
            ["PROCESSOR_NoiseFilter_Enabled"] = "true",
            ["PROCESSOR_PhishingClassifier_Enabled"] = "true",
            ["PROCESSOR_PhishingClassifier_Confidence"] = "", // Empty means use global threshold
            ["PROCESSOR_PhishingLabeler_Enabled"] = "true",
            ["PROCESSOR_TestKeyword_Enabled"] = "true"
        };
    }
}
