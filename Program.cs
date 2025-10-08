using Azure.Identity;
using Microsoft.Extensions.Azure;
using TechoramaOpenAI.Models;
using TechoramaOpenAI.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

builder.Services.Configure<OpenAISettings>(
    builder.Configuration.GetSection("OpenAI"));
builder.Services.AddSingleton<OpenAIService>();

builder.Services.Configure<AzureOpenAISettings>(
    builder.Configuration.GetSection("Azure:OpenAI"));
builder.Services.AddSingleton<AzureOpenAIService>();

builder.Services.AddAzureClients(configureClients: c =>
{
    IConfigurationSection keyVaultConfig = 
        builder.Configuration.GetSection("Azure:KeyVault");
    c.AddSecretClient(keyVaultConfig);

    DefaultAzureCredential credential = new(
        DefaultAzureCredential.DefaultEnvironmentVariableName);
    c.UseCredential(credential);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.Run();
