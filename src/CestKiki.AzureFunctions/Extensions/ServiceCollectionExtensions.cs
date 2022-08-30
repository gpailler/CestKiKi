using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CestKiki.AzureFunctions.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddOptionsAndBind<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        services
            .AddOptions<TOptions>()
            .Configure<IConfiguration>((options, configuration) => configuration.GetSection(sectionName).Bind(options));
    }
}