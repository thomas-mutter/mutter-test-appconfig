using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using Serilog;
using Serilog.Events;

namespace WorkerApp;

public class Program
{
    private const string FeatureName = "my-worker-is-running";

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

        builder.ConfigureHostConfiguration(configHost =>
        {
            configHost.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            configHost.AddUserSecrets<Program>(optional: true, reloadOnChange: true);
            configHost.AddCommandLine(args);
        });

        builder.ConfigureAppConfiguration((hostingContext, config) =>
        {
            string appConfigUrl = hostingContext.Configuration["AppConfigUrl"]
                ?? throw new InvalidOperationException("Please add AppConfigUrl to appsettings.json");

            config.AddAzureAppConfiguration(options =>
            {
                options.Connect(new Uri(appConfigUrl), new DefaultAzureCredential());

                options.UseFeatureFlags(featureFlagOptions =>
                {
                    featureFlagOptions.Select(FeatureName, LabelFilter.Null);
                    featureFlagOptions.SetRefreshInterval(TimeSpan.FromSeconds(30));
                });
            });
        });

        builder.ConfigureServices((hostContext, services) =>
        {
            services.AddAzureAppConfiguration();
            services.AddFeatureManagement();
        });

        IHost host = builder.Build();
        IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        CancellationToken cancellationToken = lifetime.ApplicationStopping;

        IVariantFeatureManager featureManager = host.Services.GetRequiredService<IVariantFeatureManager>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                bool featureMyWorkerIsRuning = await featureManager.IsEnabledAsync(FeatureName, cancellationToken);

                Console.WriteLine($"{DateTime.Now:HH:mm:ss} Feature {FeatureName} Value: {featureMyWorkerIsRuning}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

