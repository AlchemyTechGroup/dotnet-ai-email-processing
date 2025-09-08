# AI Gmail Labeler

An intelligent email security system that automatically analyzes Gmail messages for phishing attempts using AI classification via Ollama and applies labels accordingly.

## Todo:
- [ ] add recruiter labeler,

## Features

- **Desktop OAuth Authentication**: Secure Gmail access with token persistence
- **AI-Powered Classification**: Uses local Ollama models to detect phishing emails
- **Extensible Pipeline**: Modular processor architecture for easy customization
- **Smart Filtering**: Automatic deduplication and noise filtering
- **Privacy-Aware**: Configurable logging with PII protection
- **Resilient Operation**: Exponential backoff, error handling, and graceful shutdown

## Prerequisites

### Required Software
- **.NET 9.0** or later
- **Ollama** running on your network at `192.168.4.23:11434` (or configure different URL)
- **Gmail API Access** with Desktop OAuth credentials

### Ollama Setup
1. Install Ollama from [https://ollama.ai](https://ollama.ai)
2. Pull a compatible model: `ollama pull llama3.1:8b` (or your preferred model)
3. Ensure Ollama is running and accessible at the configured URL

### Gmail API Setup
1. Go to the [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select an existing one
3. Enable the Gmail API
4. Create OAuth 2.0 credentials (Desktop Application type)
5. Download the credentials JSON file

## Installation & Setup

### 1. Clone and Build
```bash
git clone <repository-url>
cd AiGmailLabeler
dotnet build
```

### 2. Configuration Setup
1. Copy your Gmail OAuth credentials to `secrets/client_secret_desktop.json`
2. Create a `.env` file in the project root with your configuration:

```bash
# Path to your Google OAuth Desktop client secrets JSON
GOOGLE_CLIENT_SECRETS_PATH=./secrets/client_secret_desktop.json

# Directory for token cache (gitignored)
GMAIL_TOKEN_STORE_DIR=./.tokens/gmail

# Ollama server configuration
OLLAMA_BASE_URL=http://192.168.4.23:11434
OLLAMA_MODEL=llama3.1:8b

# Polling configuration
POLL_INTERVAL_SECONDS=20
MAX_RESULTS=25
GMAIL_QUERY=newer_than:2d -label:"[AI]: Processed"

# Labels
AI_PHISHING_LABEL=[AI]: Phishing Possible
AI_PROCESSED_LABEL=[AI]: Processed

# Classification threshold (0.0 - 1.0)
CLASSIFY_CONFIDENCE_THRESHOLD=0.6

# HTTP and retry configuration
HTTP_TIMEOUT_SECONDS=30
BACKOFF_MIN_SECONDS=5
BACKOFF_MAX_SECONDS=60

# Logging configuration
LOG_LEVEL=Information
VERBOSE_MODE=false

# Processing configuration
BODY_MAX_KB=128
PROCESSOR_ORDER=Deduplicate,NoiseFilter,PhishingClassifier,PhishingLabeler

# Processor-specific configuration
PROCESSOR_Deduplicate_ENABLED=true
PROCESSOR_NoiseFilter_ENABLED=true
PROCESSOR_PhishingClassifier_ENABLED=true
PROCESSOR_PhishingClassifier_CONFIDENCE=0.6
PROCESSOR_PhishingLabeler_ENABLED=true
```

### 3. First Run
```bash
cd src/AiGmailLabeler.Console
dotnet run
```

On first run:
- A browser will open for Gmail OAuth consent
- Grant the requested permissions (gmail.modify scope only)
- The application will create required labels in Gmail
- Processing will begin automatically

## Usage

### Normal Operation
Simply run the application:
```bash
dotnet run
```

The application will:
1. Poll Gmail for new messages matching the configured query
2. Process each message through the configured pipeline
3. Apply appropriate labels based on classification results
4. Log all activities with configurable detail levels

### Graceful Shutdown
Press `Ctrl+C` to initiate graceful shutdown. The application will:
- Complete processing of the current message
- Stop polling for new messages
- Clean up resources
- Exit cleanly

## Configuration Reference

### Core Settings
- `GOOGLE_CLIENT_SECRETS_PATH`: Path to Gmail OAuth credentials JSON
- `GMAIL_TOKEN_STORE_DIR`: Directory for storing authentication tokens
- `OLLAMA_BASE_URL`: URL of your Ollama server
- `OLLAMA_MODEL`: Model name for classification

### Polling & Query Settings
- `POLL_INTERVAL_SECONDS`: How often to check for new emails (default: 20)
- `MAX_RESULTS`: Maximum messages to process per poll (default: 25)
- `GMAIL_QUERY`: Gmail search query for filtering messages

### Classification Settings
- `CLASSIFY_CONFIDENCE_THRESHOLD`: Minimum confidence for phishing label (0.0-1.0)
- `BODY_MAX_KB`: Maximum email body size to analyze (default: 128KB)

### Processor Configuration
- `PROCESSOR_ORDER`: Comma-separated list of processors to run
- `PROCESSOR_<Name>_ENABLED`: Enable/disable specific processors
- `PROCESSOR_PhishingClassifier_CONFIDENCE`: Per-processor confidence override

### Logging & Privacy
- `LOG_LEVEL`: Minimum log level (Trace, Debug, Information, Warning, Error)
- `VERBOSE_MODE`: When true, logs full email details (privacy risk)

## Architecture

### Pipeline Processing
Messages flow through configurable processors in order:

1. **DeduplicateProcessor**: Skips already-processed messages
2. **NoiseFilterProcessor**: Filters out automated/bulk emails
3. **PhishingClassifierProcessor**: AI classification via Ollama
4. **PhishingLabelerProcessor**: Applies labels based on classification

### Extensibility
Add new processors by:

1. Implementing `IEmailProcessor` interface
2. Registering in DI container
3. Adding to `PROCESSOR_ORDER` configuration
4. Optional: Adding processor-specific configuration

Example new processor:
```csharp
public class CustomProcessor : IEmailProcessor
{
    public async Task<ProcessorOutcome> ProcessAsync(
        MessageContext context,
        CancellationToken cancellationToken = default)
    {
        // Your processing logic here
        return ProcessorOutcome.Handled("Custom processing completed");
    }
}
```

## Troubleshooting

### Authentication Issues
- **"Authentication required"**: Delete `.tokens/` directory and re-run
- **"Client secrets not found"**: Verify `GOOGLE_CLIENT_SECRETS_PATH` points to valid JSON
- **"Insufficient permissions"**: Ensure Gmail API is enabled in Google Cloud Console

### Ollama Connectivity
- **"Connection refused"**: Check if Ollama is running at configured URL
- **"Model not found"**: Pull required model with `ollama pull <model-name>`
- **"Timeout errors"**: Increase `HTTP_TIMEOUT_SECONDS` or check network connectivity

### Gmail API Limits
- **"Quota exceeded"**: Reduce `MAX_RESULTS` or increase `POLL_INTERVAL_SECONDS`
- **"Rate limited"**: Application automatically handles rate limits with exponential backoff

### Processing Issues
- **"No messages processed"**: Check `GMAIL_QUERY` syntax and date ranges
- **"Classification errors"**: Verify Ollama model compatibility and response format
- **"Label not applied"**: Check confidence threshold settings

### Performance Tuning
- Adjust `POLL_INTERVAL_SECONDS` based on email volume
- Modify `MAX_RESULTS` to balance responsiveness vs API usage
- Set `BODY_MAX_KB` based on processing requirements
- Use `LOG_LEVEL=Warning` for production to reduce log volume

## Security Considerations

### Secrets Management
- Never commit `.env` files or credentials to source control
- Store `client_secret_desktop.json` outside the project directory if possible
- Set restrictive permissions on token directories

### Privacy Protection
- Keep `VERBOSE_MODE=false` in production
- Configure appropriate `LOG_LEVEL` to avoid PII exposure
- Monitor log files for sensitive information

### Network Security
- Run Ollama on a trusted network or use authentication
- Consider firewall rules for Ollama access
- Use HTTPS for Ollama if exposed over public networks

## Development

### Project Structure
```
src/
├── AiGmailLabeler.Console/     # Main application entry point
└── AiGmailLabeler.Core/        # Core business logic
    ├── Configuration/          # Configuration management
    ├── Services/              # Core services (Gmail, Ollama, etc.)
    ├── Processors/            # Email processing pipeline
    ├── Models/                # Data models
    └── Interfaces/            # Contracts and abstractions

tests/
└── AiGmailLabeler.Tests/      # Unit tests
```

### Building from Source
```bash
# Restore dependencies
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run application
cd src/AiGmailLabeler.Console
dotnet run
```

### Adding New Processors
See the extensibility section above. All processors should:
- Be idempotent (safe to run multiple times)
- Handle errors gracefully
- Provide meaningful logging
- Include appropriate unit tests

## License

[Specify your license here]

## Contributing

[Add contribution guidelines here]
