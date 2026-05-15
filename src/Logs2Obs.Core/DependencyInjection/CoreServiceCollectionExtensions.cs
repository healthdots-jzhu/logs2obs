using System.Reflection;
using FluentValidation;
using Logs2Obs.Core.Abstractions;
using Logs2Obs.Core.Graphs;
using Logs2Obs.Core.Query;
using Logs2Obs.Core.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Logs2Obs.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddLogs2ObsCore(this IServiceCollection services)
    {
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));

        services.AddSingleton<IValidator<Models.LogEntryDto>, LogEntryDtoValidator>();

        services.AddSingleton<GraphSuggestionEngine>();
        services.AddSingleton<ISqlSafetyValidator, SqlSafetyValidator>();
        services.AddSingleton<QueryTierRouter>();

        return services;
    }
}
