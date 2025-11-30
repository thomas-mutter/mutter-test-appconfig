using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerApp.Configuration;

namespace WorkerApp;

public class AppConfigEventRefreshWorker(
    IAzureClientFactory<ServiceBusClient> serviceBusClientFactory,
    IAzureClientFactory<ServiceBusAdministrationClient> adminClientFactory,
    AppConfig appConfig,
    IConfigurationRefresherProvider refreshProvider,
    ILogger<AppConfigEventRefreshWorker> log) : BackgroundService
{
    public static string ServiceBusClientName => "ConfigRefresh";

    private ServiceBusProcessor? Processor { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ServiceBusAdministrationClient adminClient = adminClientFactory.CreateClient(ServiceBusClientName);
        await EnsureTopicSubscriptionAsync(adminClient);

        ServiceBusClient serviceBusClient = serviceBusClientFactory.CreateClient(ServiceBusClientName);

        ServiceBusProcessorOptions options = new()
        {
            PrefetchCount = 1,
            MaxConcurrentCalls = 1
        };
        Processor = serviceBusClient.CreateProcessor(appConfig.ChangeNotificationTopicName, WorkerIdentifier, options);
        Processor.ProcessMessageAsync += ProcessMessageAsync;
        Processor.ProcessErrorAsync += ProcessErrorAsync;

        await Processor.StartProcessingAsync(stoppingToken);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        log.LogError(args.Exception, "Error in Service Bus Processor {Identifier}/{EntityPath}", args.Identifier, args.EntityPath);
        return Task.CompletedTask;
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs arg)
    {
        try
        {
            CloudEvent ce = CloudEvent.Parse(arg.Message.Body)
                ?? throw new InvalidOperationException("Message body does not contain a cloud event!");

            ce.TryGetSystemEventData(out object eventData);

            AppConfigChanged configChangedEvent = ce.Data?.ToObjectFromJson<AppConfigChanged>()
                ?? throw new InvalidOperationException("Cloud event does not contain app config event data!");

            log.LogInformation("Received app config changed event for {Entity}", configChangedEvent.Key);

            foreach (IConfigurationRefresher refresher in refreshProvider.Refreshers)
            {
                await refresher.TryRefreshAsync(default);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unable to process message: {Type}: {Message}", ex.GetType().Name, ex.Message);
            await arg.DeadLetterMessageAsync(arg.Message, $"{ex.GetType().Name}: {ex.Message}", null, default);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Processor == null)
        {
            return;
        }

        await Processor.StopProcessingAsync(cancellationToken);
        log.LogInformation("FeatureEventRefreshWorker has stopped.");
    }

    private async Task EnsureTopicSubscriptionAsync(ServiceBusAdministrationClient adminClient)
    {

    }

    private static string WorkerIdentifier => $"{Environment.MachineName}-{Environment.ProcessId}";

    public class AppConfigChanged
    {
        public string Key { get; set; } = string.Empty;

        public string? Label { get; set; }

        public string? ETag { get; set; }

        public string? SyncToken { get; set; }
    }
}
