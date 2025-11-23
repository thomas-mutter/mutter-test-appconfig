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

    public static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddAzureAppConfiguration(azureAppConfigurationOptions =>
        {
            string appConfigUrl = builder.Configuration["AppConfigUrl"]
                ?? throw new InvalidOperationException("Please add AppConfigUrl to appsettings.json");

            azureAppConfigurationOptions.Connect(new Uri(appConfigUrl), new DefaultAzureCredential())
                    .Select("QuickStart:*", LabelFilter.Null)
                    .ConfigureRefresh(refreshOptions =>
                    {
                        refreshOptions.Register($"{SettingSection}:{SettingName}", refreshAll: true);
                    });

            azureAppConfigurationOptions.UseFeatureFlags(featureFlagOptions =>
            {
                featureFlagOptions.Select(FeatureName, LabelFilter.Null);
                //featureFlagOptions.SetRefreshInterval(TimeSpan.FromSeconds(10));
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
        // Register Azure App Configuration services to enable runtime refresh
        builder.Services.AddAzureAppConfiguration();
        builder.Services.AddFeatureManagement();
        builder.Services.AddHostedService<StatusWorker>();

        WebApplication app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();
        // Enable automatic refresh of configuration and feature flags per request
        app.UseAzureAppConfiguration();

        app.MapOpenApi("/openapi/{documentName}.yaml");
        app.MapScalarApiReference(options =>
        {
            options.DarkMode = false;
            options.Title = "TestAppConfig.WebApp API Reference";
            options.AddDocument("quickstart", "Quickstart API");
        });

        app.MapGet("/settings", GetConfigurationValuesAsync)
            .WithName("GetConfigurationValues");

        await app.RunAsync();
    }

    private static async Task<Ok<AppConfigurationValues>> GetConfigurationValuesAsync(
        [FromServices] IOptionsSnapshot<Settings> option,
        [FromServices] IVariantFeatureManager featureManager,
        [FromServices] ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        bool test1 = await featureManager.IsEnabledAsync(FeatureName, cancellationToken);
        AppConfigurationValues configValues = new(option.Value.Ort, test1, DateTimeOffset.Now);
        return TypedResults.Ok(configValues);
    }
}

public class Settings
{
    public string Ort { get; set; } = string.Empty;
}

public record AppConfigurationValues(string Ort, bool Test1, DateTimeOffset LastModified)
{
    public bool IsEqual(AppConfigurationValues other) =>
        Ort.Equals(other.Ort, StringComparison.CurrentCultureIgnoreCase) &&
        Test1 == other.Test1;

    public bool IsNotEqual(AppConfigurationValues other) => !IsEqual(other);
};

public class StatusWorker(
    IVariantFeatureManager featureManager,
    IServiceProvider services) : BackgroundService
{
    private IVariantFeatureManager FeatureManager { get; } = featureManager;

    private IServiceProvider Services { get; } = services;

    private static AppConfigurationValues CurrentSettings { get; set; } = new AppConfigurationValues(string.Empty, false, DateTimeOffset.MinValue);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = Services.CreateScope();
                ILogger<StatusWorker> log = scope.ServiceProvider.GetRequiredService<ILogger<StatusWorker>>();
                await RefreshConfigurationAsync(scope, log, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                // Ignore
            }
        }
    }

    private async Task RefreshConfigurationAsync(IServiceScope scope, ILogger<StatusWorker> log, CancellationToken stoppingToken)
    {
        // Obtain the IConfigurationRefresher from the provider and trigger a refresh.
        // This causes the registered keys and feature flags to be checked and updated if changed.
        try
        {
            IConfigurationRefresherProvider refresherProvider = scope.ServiceProvider.GetRequiredService<IConfigurationRefresherProvider>();
            foreach (IConfigurationRefresher refresher in refresherProvider.Refreshers)
            {
                bool success = await refresher.TryRefreshAsync(stoppingToken);
                if (!success)
                {
                    continue;
                }

                IOptionsSnapshot<Settings> settingOptions = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<Settings>>();
                bool test1 = await FeatureManager.IsEnabledAsync("Test1", stoppingToken);
                AppConfigurationValues newConfigValues = new(settingOptions.Value.Ort, test1, DateTimeOffset.Now);

                if (CurrentSettings.LastModified == DateTimeOffset.MinValue ||
                    newConfigValues.IsNotEqual(CurrentSettings))
                {
                    CurrentSettings = newConfigValues;
                    log.LogInformation("Configuration changed Ort={Ort}, Test1={Test1}", CurrentSettings.Ort, CurrentSettings.Test1);
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Ignore
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Error while trying to refresh Azure App Configuration.");
        }
    }
}
