using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Serilog;
using Serilog.Events;
using WorkerApp.Configuration;

namespace WorkerApp;

public class Program
{

    private static async Task Main(string[] args)
    {
        // Category: {SourceContext:l}
        const string logTemplate = "[{Timestamp:HH:mm:ss} {Level:u3} {SourceContext:l}] {Message:lj}{NewLine}";
        Log.Logger = new LoggerConfiguration().MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Azure", LogEventLevel.Information)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate: logTemplate)
            .CreateLogger();

        Log.Logger.Information("Starting WorkerApp");

        IHostBuilder builder = Host.CreateDefaultBuilder(args);

        builder.UseSerilog();

        // 2 Step configuration:
        // 1. ConfigureHostConfiguration: appsettings.json, UserSecrets, CommandLine
        builder.ConfigureHostConfiguration(configHost =>
        {
            configHost.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            configHost.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
            configHost.AddCommandLine(args);
        });

        // 2. ConfigureAppConfiguration: Azure App Configuration
        builder.ConfigureAppConfiguration((hostingContext, config) =>
        {
            AppConfig appConfig = hostingContext.Configuration.GetAppConfig();

            config.AddAzureAppConfiguration(options =>
            {
                options.Connect(appConfig.Uri, new DefaultAzureCredential());

                options.UseFeatureFlags(featureFlagOptions =>
                {
                    featureFlagOptions.Select(Worker.FeatureName, LabelFilter.Null);
                    featureFlagOptions.SetRefreshInterval(TimeSpan.FromDays(1));
                });
            });
        });

        builder.ConfigureServices((hostContext, services) =>
        {
            AppConfig appConfig = hostContext.Configuration.GetAppConfig();
            services.AddSingleton(appConfig);

            services.AddAzureAppConfiguration();
            services.AddFeatureManagement();

            services.AddAzureClients(builder =>
            {
                builder.UseCredential(new DefaultAzureCredential());
                builder
                    .AddServiceBusClientWithNamespace(appConfig.ChangeNotificationServiceBusNamespace)
                    .WithName(AppConfigEventRefreshWorker.ServiceBusClientName);
            });

            services.AddHostedService<AppConfigEventRefreshWorker>();
            services.AddHostedService<Worker>();
        });

        IHost host = builder.Build();
        await host.RunAsync();
    }
}

