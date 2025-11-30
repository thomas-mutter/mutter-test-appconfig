using Microsoft.Extensions.Configuration;

namespace WorkerApp.Configuration;

public static class AppConfigExtensions
{
    public static AppConfig GetAppConfig(this IConfiguration configuration)
    {
        AppConfig appConfig = configuration.GetSection("AppConfig").Get<AppConfig>()
            ?? throw new InvalidOperationException("AppConfig section is missing in configuration.");
        appConfig.Validate();
        return appConfig;
    }
}
