using Azure.Data.Tables;

using CestKiki.AzureFunctions.Extensions;
using CestKiki.AzureFunctions.Helpers;
using CestKiki.AzureFunctions.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NodaTime;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(builder =>
    {
        builder.Services.AddHttpClient();

        // Singletons
        builder.Services.AddSingleton(serviceProvider => new TableClient(
            serviceProvider.GetRequiredService<IConfiguration>().GetValue<string>("AzureWebJobsStorage"),
            serviceProvider.GetRequiredService<IOptions<TableOptions>>().Value.TableName));
        builder.Services.AddSingleton<IZoomSignatureHelper, ZoomSignatureHelper>();
        builder.Services.AddSingleton<IClock>(SystemClock.Instance);

        // Options
        builder.Services.AddOptionsAndBind<TableOptions>(TableOptions.Key);
        builder.Services.AddOptionsAndBind<ZoomOptions>(ZoomOptions.Key);
        builder.Services.AddOptionsAndBind<NotificationOptions>(NotificationOptions.Key);
    })
    .Build();

await host.Services.GetRequiredService<TableClient>().CreateIfNotExistsAsync();

await host.RunAsync();
