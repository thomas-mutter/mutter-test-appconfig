using Azure.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Options;
using Microsoft.FeatureManagement;
using Scalar.AspNetCore;

namespace TestAppConfig.WebApp;

public class Program
{
    private const string SettingSection = "QuickStart";
    private const string SettingName = "Ort";
    private const string FeatureName = "Test1";

    private static readonly Uri AppConfigUri = new("https://thomyconfig.azconfig.io");

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddAzureAppConfiguration(azureAppConfigurationOptions =>
        {
            azureAppConfigurationOptions.Connect(AppConfigUri, new DefaultAzureCredential())
                    .Select("QuickStart:*", LabelFilter.Null)
                    .ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register($"{SettingSection}:{SettingName}", refreshAll: true)
                            .SetRefreshInterval(TimeSpan.FromSeconds(10));
                    });

            azureAppConfigurationOptions.UseFeatureFlags(featureFlagOptions =>
            {
                featureFlagOptions.Select(FeatureName, LabelFilter.Null);
                featureFlagOptions.SetRefreshInterval(TimeSpan.FromSeconds(10));
                    
            });
        });

        builder.Services.AddOpenApi("quickstart", options =>
        {
            options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Info = new Microsoft.OpenApi.OpenApiInfo
                {
                    Title = "TestAppConfig.WebApp",
                    Version = "0.0.1",
                    Description = "A example Web API for Azure App Configuration with Feature Management",
                };
                return Task.CompletedTask;
            });
        });

        builder.Services.AddOptions();
        builder.Services.Configure<Settings>(builder.Configuration.GetSection(SettingSection));
        builder.Services.AddFeatureManagement();

        WebApplication app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.MapOpenApi("/openapi/{documentName}.yaml");
        app.MapScalarApiReference(options =>
        {
            options.DarkMode = false;
            options.Title = "TestAppConfig.WebApp API Reference";
            options.AddDocument("quickstart", "Quickstart API");
        });

        app.MapGet("/settings", GetConfigurationValuesAsync)
            .WithName("GetConfigurationValues");

        app.Run();
    }

    private static async Task<Ok<AppConfigurationValues>> GetConfigurationValuesAsync(
        [FromServices] IOptionsSnapshot<Settings> option,
        [FromServices] IVariantFeatureManager featureManager,
        CancellationToken cancellationToken)
    {
        bool test1 = await featureManager.IsEnabledAsync(FeatureName, cancellationToken);
        var configValues = new AppConfigurationValues(option.Value.Ort, test1);
        return TypedResults.Ok(configValues);
    }
}

public class Settings
{
    public string Ort { get; set; } = string.Empty;
}

public record AppConfigurationValues(string Ort, bool Test1);
