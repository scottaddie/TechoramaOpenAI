using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;
using TechoramaOpenAI.Models;

namespace TechoramaOpenAI.Services;

public class AzureOpenAIService(
    SecretClient secretClient,
    IOptions<AzureOpenAISettings> options,
    IMemoryCache cache,
    TokenCredential credential)
{
    private readonly AzureOpenAISettings _settings = options.Value;
    private readonly IMemoryCache _cache = cache;

#pragma warning disable OPENAI001
    public async Task<string> UseResponsesAsync(string prompt, bool useEntraId = false)
    {
        try
        {
            OpenAIClient openAIClient = await GetAzureOpenAIClient(useEntraId);
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.DeploymentName);
            OpenAIResponse response = await responseClient.CreateResponseAsync(prompt);

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<OpenAIClient> GetAzureOpenAIClient(bool useEntraId)
    {
        OpenAIClient client = null!;

        OpenAIClientOptions clientOptions = new()
        {
            Endpoint = new Uri($"{_settings.Endpoint}/openai/v1/"),
        };

        if (useEntraId)
        {
            BearerTokenPolicy tokenPolicy = new(credential, _settings.Scope);
            client = new(tokenPolicy, clientOptions);
        }
        else
        {
            string? apiKey = await GetAzureOpenAIApiKey() ?? throw new InvalidOperationException("Azure OpenAI API key not configured");
            client = new(new ApiKeyCredential(apiKey), clientOptions);
        }

        return client;
    }
#pragma warning restore OPENAI001

    private async Task<string?> GetAzureOpenAIApiKey()
    {
        const string AzureOpenAIKeyCacheKey = "AZURE-OPENAI-API-KEY";

        return await _cache.GetOrCreateAsync(AzureOpenAIKeyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(AzureOpenAIKeyCacheKey);
            return secret?.Value;
        });
    }
}
