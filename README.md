
# CestKiKi Azure functions
[![Azure functions workflow](https://github.com/gpailler/CestKiki/actions/workflows/main.yml/badge.svg)](https://github.com/gpailler/CestKiKi/actions/workflows/main.yml)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=gpailler_CestKiKi&metric=coverage)](https://sonarcloud.io/summary/new_code?id=gpailler_CestKiKi)


## Summary
You are running a daily stand-up meeting on Zoom with a rolling presenter and you always forget who was presenting the day before?

This project is for you!

![image](https://user-images.githubusercontent.com/3621529/188034011-41ce3b5b-8c81-42db-979e-b649b6cdf256.png)

## General design
CestKiki runs serverless, on the cloud. It uses webhooks to receive notifications about Zoom screen sharings and stores needed infos into an Azure Table. Once a day, it queries the Table to find who was sharing his screen during the stand-up and send a Slack notification.

```mermaid
sequenceDiagram
  participant A as Zoom<br><br>WebHook app
  participant B as Azure Function<br><br>ZoomWebHookFunction
  participant C as Azure<br><br>Tables
  participant D as Azure Function<br><br>NotificationFunction
  participant E as Slack<br><br>Incoming Webhook
  rect rgb(191, 223, 255)
    note right of A: Continuously
    A->>B: Send sharing_started event
    B->>C: Add sharing info entity<br>(user, start time...)
    A->>B: Send sharing_ended event
    B->>C: Update sharing info entity<br>(end time...)
  end
  rect rgb(191, 223, 255)
    note right of C: Once a day
    D->>C: Query sharing entities<br>and find who was sharing<br>his screen during the stand-up
    D->>E: Send notification
  end
```

## Configuration
### Azure
- Create a new Azure Function App (a new Storage account should be automatically created)
### Zoom
- Create a [Webhook only app](https://marketplace.zoom.us/docs/guides/build/webhook-only-app/) on the [Zoom Marketplace](https://marketplace.zoom.us/develop/create)
- Use the generated **Secret Token** for the Azure Function configuration below
- Use the *https://[AZURE_FUNCTION_NAME].azurewebsites.net/api/ZoomWebHook* URL for the **Event notification endpoint** 
- Enable **Event Subscriptions** for the following events:
  - Participant/Host left meeting
  - Meeting Sharing Started
  - Meeting Sharing Ended
### Slack
- Create a [new App](https://api.slack.com/apps) in your workspace
- Activate the [Incoming Webhooks](https://api.slack.com/messaging/webhooks), create a new Webhook for your channel and use the generated URL for the Azure Function configuration below
### Azure
- Edit the Function App Configuration and add the following settings:
  - `Notification:StandUpTimeZone = Europe/Paris` *(Zone ID from https://nodatime.org/TimeZones)*
  - `Notification:StandUpStartTime = 10:15:00`
  - `Notification:StandUpEndTime = 10:30:00`
  - `Notification:NotificationTime = 10:30:00`
  - `NotificationCron = 0 30 * * * *` *(The Azure function should run at the same time as the NotificationTime)*
  - `Notification:WebHook = https://hooks.slack.com/services/[...]` *(Slack Incoming Webhook configured above)*
  - `Zoom:MonitoredRoom = 123456789` *(Zoom room id where the stand-up occurs)*
  - `Zoom:WebHookSecret = [...]` *(Secret Token from Zoom generated above)* 
- Edit Networking -> Inbound Traffic -> Access restriction and add the [Zoom IP ranges](https://marketplace.zoom.us/docs/api-reference/webhook-reference/#ip-addresses)
- Deploy CestKiKi app on Azure

## Compilation and Deployment
CestKiKi requires the following components:
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- [Azure Functions Core Tools V4](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools)

The solution can be loaded and published as-is to the Azure Function App. A fair amount of unit tests are written as debugging Azure Function live could be tedious.
