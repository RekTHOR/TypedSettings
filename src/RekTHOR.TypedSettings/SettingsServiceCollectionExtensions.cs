using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TypedSettings;

/// <summary>
/// Extension methods for registering and reading <see cref="ISettings{TSelf}"/> types.
/// </summary>
public static class SettingsServiceCollectionExtensions
{
    /// <summary>
    /// Binds <typeparamref name="T"/> to its configuration section, validates its DataAnnotations on startup,
    /// and registers the plain settings object in DI with the lifetime implied by <paramref name="reloadMode"/>.
    /// </summary>
    /// <param name="services">The service collection to add the settings to.</param>
    /// <param name="reloadMode">Controls how configuration changes are picked up, and with it the DI lifetime.</param>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="reloadMode"/> is not a defined <see cref="SettingsReloadMode"/> value.</exception>
    public static IServiceCollection AddSettings<T>(
        this IServiceCollection services,
        SettingsReloadMode reloadMode = SettingsReloadMode.None)
        where T : class, ISettings<T>
    {
        services
            .AddOptionsWithValidateOnStart<T>()
            .BindConfiguration(T.SectionName)
            .ValidateDataAnnotations();

        services.Add(reloadMode switch
        {
            SettingsReloadMode.None =>
                ServiceDescriptor.Singleton(sp => sp.GetRequiredService<IOptions<T>>().Value),
            SettingsReloadMode.PerScope =>
                ServiceDescriptor.Scoped(sp => sp.GetRequiredService<IOptionsSnapshot<T>>().Value),
            SettingsReloadMode.Always =>
                ServiceDescriptor.Transient(sp => sp.GetRequiredService<IOptionsMonitor<T>>().CurrentValue),
            _ => throw new ArgumentOutOfRangeException(nameof(reloadMode), reloadMode, null)
        });

        return services;
    }

    /// <summary>
    /// Reads and validates <typeparamref name="T"/> directly from the configuration, without a DI container.
    /// Useful while the host is still being configured. Falls back to a default instance if the section is missing.
    /// </summary>
    /// <param name="configuration">The configuration to read the settings section from.</param>
    /// <typeparam name="T">The settings type.</typeparam>
    /// <returns>The bound and validated settings instance.</returns>
    /// <exception cref="OptionsValidationException">The settings fail DataAnnotations validation.</exception>
    public static T GetSettings<T>(this IConfiguration configuration)
        where T : class, ISettings<T>, new()
    {
        T settings = configuration.GetSection(T.SectionName).Get<T>() ?? new T();

        List<ValidationResult> results = [];

        if (!Validator.TryValidateObject(settings, new ValidationContext(settings), results, true))
        {
            throw new OptionsValidationException(
                T.SectionName,
                typeof(T),
                results.Select(r => r.ErrorMessage ?? $"'{T.SectionName}' is invalid."));
        }

        return settings;
    }
}
