using System.ComponentModel.DataAnnotations;

namespace AiGmailLabeler.Core.Configuration;

public class AppConfiguration
{
    [Required]
    public string GoogleClientSecretsPath { get; set; } = string.Empty;

    [Required]
    public string GmailTokenStoreDir { get; set; } = string.Empty;

    [Required]
    public string OllamaBaseUrl { get; set; } = "http://192.168.4.23:11434";

    [Required]
    public string OllamaModel { get; set; } = "llama3.1:8b";

    [Range(1, int.MaxValue)]
    public int PollIntervalSeconds { get; set; } = 20;

    [Range(1, 1000)]
    public int MaxResults { get; set; } = 25;

    [Required]
    public string GmailQuery { get; set; } = "newer_than:2d -label:\"[AI]: Processed\"";

    [Required]
    public string AiPhishingLabel { get; set; } = "[AI]: Phishing Possible";

    [Required]
    public string AiProcessedLabel { get; set; } = "[AI]: Processed";

    [Range(0.0, 1.0)]
    public double ClassifyConfidenceThreshold { get; set; } = 0.6;

    [Range(1, int.MaxValue)]
    public int HttpTimeoutSeconds { get; set; } = 30;

    [Range(1, int.MaxValue)]
    public int BackoffMinSeconds { get; set; } = 5;

    [Range(1, int.MaxValue)]
    public int BackoffMaxSeconds { get; set; } = 60;

    [Required]
    public string LogLevel { get; set; } = "Information";

    public bool VerboseMode { get; set; } = false;

    [Range(1, int.MaxValue)]
    public int BodyMaxKb { get; set; } = 128;

    public string ProcessorOrder { get; set; } = "Deduplicate,NoiseFilter,PhishingClassifier,PhishingLabeler";

    public ProcessorConfiguration Processors { get; set; } = new();

    public IReadOnlyList<string> GetProcessorOrderList() =>
        ProcessorOrder.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class ProcessorConfiguration
{
    public DeduplicateProcessorConfig Deduplicate { get; set; } = new();
    public NoiseFilterProcessorConfig NoiseFilter { get; set; } = new();
    public PhishingClassifierProcessorConfig PhishingClassifier { get; set; } = new();
    public PhishingLabelerProcessorConfig PhishingLabeler { get; set; } = new();
    public TestKeywordProcessorConfig TestKeyword { get; set; } = new();
}

public class DeduplicateProcessorConfig
{
    public bool Enabled { get; set; } = true;
}

public class NoiseFilterProcessorConfig
{
    public bool Enabled { get; set; } = true;
}

public class PhishingClassifierProcessorConfig
{
    public bool Enabled { get; set; } = true;

    [Range(0.0, 1.0)]
    public double? Confidence { get; set; } // Overrides global threshold if set
}

public class PhishingLabelerProcessorConfig
{
    public bool Enabled { get; set; } = true;
}

public class TestKeywordProcessorConfig
{
    public bool Enabled { get; set; } = true;
}
