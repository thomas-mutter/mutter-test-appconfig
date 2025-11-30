namespace WorkerApp.Configuration;

/// <summary>
/// Settings to access Azure App Configuration.
/// </summary>
public class AppConfig
{
    /// <summary>
    /// Gets or sets the Azure App Configuration URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Fully qualified namespace of the Service Bus used for change notifications.
    /// </summary>
    public string ChangeNotificationServiceBusNamespace { get; set; } = string.Empty;

    /// <summary>
    /// Name of the topic where Azure App Configuration sends change notifications via Event Grid.
    /// </summary>
    public string ChangeNotificationTopicName { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new InvalidOperationException("AppConfig Url is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ChangeNotificationServiceBusNamespace))
        {
            throw new InvalidOperationException("ChangeNotificationServiceBusNamespace is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ChangeNotificationTopicName))
        {
            throw new InvalidOperationException("ChangeNotificationTopicName is not configured.");
        }

        if (Url.Contains('<'))
        {
            throw new InvalidOperationException("AppConfig contains sample values. Configure real values properly.");
        }
    }

    public Uri Uri => new(Url);
}
