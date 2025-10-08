using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using TechoramaOpenAI.Models;

namespace TechoramaOpenAI.Services;

public class OpenAIService(SecretClient secretClient, IOptions<OpenAISettings> options)
{
    private readonly OpenAISettings _settings = options.Value;

#pragma warning disable OPENAI001
    public async Task<string> UseResponsesAsync(string prompt)
    {
        try
        {
            string? openAiApiKey = await GetOpenAIApiKey();
            if (openAiApiKey == null) return "Error: OpenAI API key not configured";

            OpenAIClient openAIClient = new(new ApiKeyCredential(openAiApiKey));
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.ModelName);
            OpenAIResponse response = await responseClient.CreateResponseAsync(prompt);

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> UseResponsesWithMcpAsync(string prompt)
    {
        string? openAiApiKey = await GetOpenAIApiKey();
        if (openAiApiKey == null) return "Error: OpenAI API key not configured";
        
        string? stripeApiKey = await GetStripeApiKey();
        if (stripeApiKey == null) return "Error: Stripe API key not configured";

        try
        {
            OpenAIResponseClient client = new(_settings.ModelName, openAiApiKey);

            ResponseCreationOptions options = new()
            {
                Tools =
                {
                    new McpTool(serverLabel: "stripe", serverUri: new Uri("https://mcp.stripe.com"))
                    {
                        AuthorizationToken = stripeApiKey,
                        ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval),
                    },
                }
            };

            OpenAIResponse response = await client.CreateResponseAsync(prompt, options);
            string output = response.GetOutputText();

            return string.IsNullOrEmpty(output) ? "Response was empty or null" : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
#pragma warning restore OPENAI001

    private async Task<string?> GetOpenAIApiKey()
    {
        KeyVaultSecret secret = await secretClient.GetSecretAsync("OPENAI-API-KEY");
        return secret?.Value;
    }

    private async Task<string?> GetStripeApiKey()
    {
        KeyVaultSecret secret = await secretClient.GetSecretAsync("STRIPE-OAUTH-ACCESS-TOKEN");
        return secret?.Value;
    }
}
