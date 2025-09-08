using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using AiGmailLabeler.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace AiGmailLabeler.Core.Services;

public class GmailAuth
{
    private readonly AppConfiguration _config;
    private readonly ILogger<GmailAuth> _logger;
    private readonly string[] _scopes = [GmailService.Scope.GmailModify];

    public GmailAuth(AppConfiguration config, ILogger<GmailAuth> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<GmailService> GetGmailServiceAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Gmail authentication");

        // Validate client secrets file exists
        if (!File.Exists(_config.GoogleClientSecretsPath))
        {
            throw new FileNotFoundException($"Google client secrets file not found at: {_config.GoogleClientSecretsPath}");
        }

        // Create token store directory if it doesn't exist
        var tokenStoreDir = Path.GetFullPath(_config.GmailTokenStoreDir);
        Directory.CreateDirectory(tokenStoreDir);

        // Log token directory location for security awareness
        _logger.LogInformation("Using token store directory: {TokenDir}", tokenStoreDir);

        UserCredential credential;

        try
        {
            using var stream = new FileStream(_config.GoogleClientSecretsPath, FileMode.Open, FileAccess.Read);

            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                _scopes,
                "user", // Use fixed user ID for token storage
                cancellationToken,
                new FileDataStore(tokenStoreDir, fullPath: true)
            );

            _logger.LogInformation("Gmail authentication successful");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authenticate with Gmail");
            throw new InvalidOperationException("Gmail authentication failed. See inner exception for details.", ex);
        }

        // Create Gmail service
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "AI Gmail Labeler",
            GZipEnabled = true
        });

        return service;
    }

    public async Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenStoreDir = Path.GetFullPath(_config.GmailTokenStoreDir);
            if (!Directory.Exists(tokenStoreDir))
                return false;

            var dataStore = new FileDataStore(tokenStoreDir, fullPath: true);
            var token = await dataStore.GetAsync<TokenResponse>("user");

            return token != null && !string.IsNullOrEmpty(token.AccessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking authentication status");
            return false;
        }
    }

    public async Task ClearTokensAsync()
    {
        try
        {
            var tokenStoreDir = Path.GetFullPath(_config.GmailTokenStoreDir);
            if (Directory.Exists(tokenStoreDir))
            {
                var dataStore = new FileDataStore(tokenStoreDir, fullPath: true);
                await dataStore.ClearAsync();
                _logger.LogInformation("Cleared stored authentication tokens");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear authentication tokens");
            throw;
        }
    }
}
