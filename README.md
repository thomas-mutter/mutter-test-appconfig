# TestAppConfigWebApp

Test application to validate functionality of Azure App Configuration with settings and feature flags.

## Prerequisites
- Add an Azure App Configuration instance to your Azure subscription.
- Add Key `QuickStart:Ort` with value in configuration explorer.
- Add Key `Test1` in feature manager

Configure the URL of your Azure App Configuration instance in the user secrets file. Sample can be found in `appsettings.json`.

# Docs
## Polling Configuration
https://learn.microsoft.com/en-us/azure/azure-app-configuration/enable-dynamic-configuration-dotnet-core

## Push Config Changes
https://learn.microsoft.com/en-us/azure/azure-app-configuration/enable-dynamic-configuration-dotnet-core-push-refresh?tabs=windowscommandprompt

## Options
https://learn.microsoft.com/en-us/dotnet/core/extensions/options

