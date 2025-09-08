using AiGmailLabeler.Core.Configuration;
using AiGmailLabeler.Core.Services;
using AiGmailLabeler.Core.Processors;
using AiGmailLabeler.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace AiGmailLabeler.Console;

class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Build and validate configuration first
            var appConfig = AppConfigurationBuilder.BuildAndValidate();

            // Create host with all dependencies
            var host = CreateHost(args, appConfig);

            // Run the application
            await host.RunAsync();
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Configuration validation failed"))
        {
            System.Console.WriteLine($"Configuration Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"Fatal Error: {ex.Message}");
            return 1;
        }
    }

    private static IHost CreateHost(string[] args, AppConfiguration appConfig)
    {
        var builder = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Register configuration
                services.AddSingleton(appConfig);
                services.AddSingleton<IConfiguration>(AppConfigurationBuilder.Build());

                // Configure logging
                services.AddLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole(options =>
                    {
                        options.IncludeScopes = true;
                        options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                    });

                    if (Enum.TryParse<LogLevel>(appConfig.LogLevel, out var logLevel))
                    {
                        logging.SetMinimumLevel(logLevel);
                    }
                });

                // Register HTTP client for Ollama
                services.AddHttpClient<OllamaClient>();

                // Register core services
                services.AddSingleton<MimeTextExtractor>();
                services.AddSingleton<GmailAuth>();
                // GmailClient will be created manually in the worker after authentication

                services.AddTransient<PipelineCoordinator>();

                // Register processors
                services.AddTransient<DeduplicateProcessor>();
                services.AddTransient<NoiseFilterProcessor>();
                services.AddTransient<PhishingClassifierProcessor>();
                services.AddTransient<PhishingLabelerProcessor>();
                services.AddTransient<TestKeywordProcessor>();

                // Register the main worker service
                services.AddHostedService<EmailProcessingWorker>();
            });

        return builder.Build();
    }
}