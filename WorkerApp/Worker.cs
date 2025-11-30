using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;

namespace WorkerApp;

public class Worker(
    IVariantFeatureManager featureManager,
    ILogger<Worker> log) : BackgroundService
{
    public static string FeatureName => "my-worker-is-running";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool featureMyWorkerIsRuning = await featureManager.IsEnabledAsync(FeatureName, stoppingToken);

            log.LogInformation("Feature {FeatureName} Value: {featureMyWorkerIsRuning}", FeatureName, featureMyWorkerIsRuning);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}