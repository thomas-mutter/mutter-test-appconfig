using Azure.Messaging.EventGrid;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwissLife.Slkv.Framework.Extensions.ServiceBus.Administration;
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

    private readonly ServiceBusAdministrationClient adminClient = adminClientFactory.CreateClient(ServiceBusClientName);

    private readonly ServiceBusClient serviceBusClient = serviceBusClientFactory.CreateClient(ServiceBusClientName);

    private ServiceBusProcessor? Processor { get; set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await adminClient.EnsureSubscriptionAsync(appConfig.ChangeNotificationTopicName, WorkerIdentifier, null, null, log, stoppingToken);

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
            // Build an EventGridEvent instance from the notification message.
            EventGridEvent eventGridEvent = EventGridEvent.Parse(BinaryData.FromBytes(arg.Message.Body));

            // Create a PushNotification instance from the Event Grid event.
            if (!eventGridEvent.TryCreatePushNotification(out PushNotification? pushNotification) || pushNotification == null)
            {
                log.LogWarning("Received EventGridEvent could not be transformed into PushNotification. Event subject={Subject}", eventGridEvent.Subject);
                return;
            }

            foreach (IConfigurationRefresher? refresher in refreshProvider.Refreshers)
            {
                await refresher.TryRefreshAsync();
            }

            log.LogInformation("Received app config changed event for {Uri}.", pushNotification.ResourceUri);
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

        await adminClient.DeleteSubscriptionAsync(appConfig.ChangeNotificationTopicName, WorkerIdentifier, cancellationToken);
    }

    private static string WorkerIdentifier => $"{Environment.MachineName}-{Environment.ProcessId}".ToLowerInvariant();
}
