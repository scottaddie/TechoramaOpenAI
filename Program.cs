using Azure.Identity;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

#pragma warning disable OPENAI001

// DEMO 1.1: Azure OpenAI Responses w/ Entra or API key
//await UseAzureOpenAIResponses();
// DEMO 1.2: OpenAI Responses w/ API key
await UseOpenAiResponses();

// DEMO 2: OpenAI Responses w/ Stripe MCP tool
//await UseOpenAIResponsesWithRemoteMcpTool();

static async Task UseAzureOpenAiResponses()
{
    OpenAIClient openAIClient = GetOpenAIClient(false);
    OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient("codex-mini-2");
    OpenAIResponse response = await responseClient.CreateResponseAsync("Say 'This is a test.'");

    Console.WriteLine(response.GetOutputText());
}

static async Task UseOpenAiResponses()
{
    string apiKey = GetOpenAiApiKey()!;
    OpenAIClient openAIClient = new(
        credential: new ApiKeyCredential(apiKey));
    Console.WriteLine("Using OpenAI API key");
    OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient("gpt-5");
    OpenAIResponse response = await responseClient.CreateResponseAsync("Say 'This is a test.'");

    Console.WriteLine(response.GetOutputText());
}

static OpenAIClient GetOpenAIClient(bool useEntra)
{
    const string azureOpenAiEndpoint = "https://openai-jc4hwqn6g2r7g.openai.azure.com";
    OpenAIClient client = null!;

    OpenAIClientOptions clientOptions = new()
    {
        Endpoint = new Uri($"{azureOpenAiEndpoint}/openai/v1/"),
    };

    if (useEntra)
    {
        Console.WriteLine("Using Entra ID");
        VisualStudioCredentialOptions credentialOptions = new()
        {
            TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
        };
        VisualStudioCredential credential = new(credentialOptions);
        BearerTokenPolicy tokenPolicy = new(
            tokenProvider: credential,
            scope: "https://cognitiveservices.azure.com/.default");

        client = new(
            authenticationPolicy: tokenPolicy,
            options: clientOptions);
    }
    else
    {
        Console.WriteLine("Using Azure OpenAI API key");
        string apiKey = GetAzureOpenAiApiKey()!;
        client = new(
            credential: new ApiKeyCredential(apiKey),
            options: clientOptions);
    }

    return client;
}

static async Task UseOpenAiResponsesWithRemoteMcpTool()
{
    string? openAiApiKey = GetOpenAiApiKey();
    string? stripeApiKey = GetStripeApiKey();

    try
    {
        OpenAIResponseClient client = new(model: "gpt-5", apiKey: openAiApiKey);

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

        Console.WriteLine("Sending request to OpenAI with Stripe MCP tools...");
        OpenAIResponse response = await client.CreateResponseAsync("List my Stripe products", options);

        string output = response.GetOutputText();

        if (string.IsNullOrEmpty(output))
            Console.WriteLine("Response was empty or null");
        else
            Console.WriteLine($"Response: {output}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex.Message}");
        Console.WriteLine($"Exception type: {ex.GetType().Name}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}

#pragma warning restore OPENAI001

static string? GetAzureOpenAiApiKey()
{
    string? apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("Error: AZURE_OPENAI_API_KEY environment variable is not set.");
        return null;
    }
    return apiKey;
}

static string? GetOpenAiApiKey()
{
    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
        return null;
    }

    return apiKey;
}

static string? GetStripeApiKey()
{
    string? authToken = Environment.GetEnvironmentVariable("STRIPE_OAUTH_ACCESS_TOKEN");

    if (string.IsNullOrEmpty(authToken))
    {
        Console.WriteLine("Error: STRIPE_OAUTH_ACCESS_TOKEN environment variable is not set.");
        return null;
    }

    return authToken;
}
