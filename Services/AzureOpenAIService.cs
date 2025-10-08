using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;
using TechoramaOpenAI.Models;

namespace TechoramaOpenAI.Services;

public class AzureOpenAIService(SecretClient secretClient, IOptions<AzureOpenAISettings> options)
{
    private readonly AzureOpenAISettings _settings = options.Value;

#pragma warning disable OPENAI001
    public async Task<string> UseResponsesAsync(string prompt, bool useEntraId = false)
    {
        try
        {
            OpenAIClient openAIClient = await GetAzureOpenAIClient(useEntraId);
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.DeploymentName);
            //OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient("gpt-5-mini");
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
            //TODO: find a way to pass this cred in from Program.cs so it's reused and uses token cache
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
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
        KeyVaultSecret secret = await secretClient.GetSecretAsync("AZURE-OPENAI-API-KEY");
        return secret?.Value;
    }
}
