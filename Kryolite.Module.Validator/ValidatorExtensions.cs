using Microsoft.Extensions.DependencyInjection;

namespace Kryolite.Module.Validator;

public static class ValidatorModuleExtensions
{
    public static IServiceCollection AddValidatorModule(this IServiceCollection services)
    {
        services.AddTransient<IGenerator, Generator>();
        services.AddSingleton<IRunner, Runner>();
        services.AddSingleton<ISlots, Slots>();
        services.AddSingleton<ISynchronizer, Synchronizer>();
        services.AddHostedService<ValidatorService>();
        return services;
    }
}