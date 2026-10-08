using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TypedSettings;

namespace RekTHOR.TypedSettings.Tests;

public class SettingsTests
{
    private sealed class MailSettings : ISettings<MailSettings>
    {
        public static string SectionName => "Mail";

        [Required] public string Host { get; set; } = "";

        [Range(1, 65535)] public int Port { get; set; } = 25;
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static ServiceProvider Build(IConfiguration config, SettingsReloadMode mode = SettingsReloadMode.None)
    {
        ServiceCollection services = new();
        services.AddSingleton(config);
        services.AddSettings<MailSettings>(mode);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSettings_BindsSectionAndResolvesPlainType()
    {
        using ServiceProvider sp = Build(Config(new Dictionary<string, string?>
            { ["Mail:Host"] = "smtp.test", ["Mail:Port"] = "587" }));

        MailSettings settings = sp.GetRequiredService<MailSettings>();

        Assert.Equal("smtp.test", settings.Host);
        Assert.Equal(587, settings.Port);
    }

    [Fact]
    public void AddSettings_InvalidValue_FailsOnResolve()
    {
        using ServiceProvider sp = Build(Config(new Dictionary<string, string?> { ["Mail:Port"] = "587" }));

        Assert.Throws<OptionsValidationException>(sp.GetRequiredService<MailSettings>);
    }

    [Fact]
    public void AddSettings_NoneMode_IsSingleton()
    {
        using ServiceProvider sp = Build(Config(new Dictionary<string, string?> { ["Mail:Host"] = "smtp.test" }));

        Assert.Same(sp.GetRequiredService<MailSettings>(), sp.GetRequiredService<MailSettings>());
    }

    [Fact]
    public void AddSettings_PerScopeMode_IsOnePerScope()
    {
        using ServiceProvider sp = Build(Config(new Dictionary<string, string?> { ["Mail:Host"] = "smtp.test" }),
            SettingsReloadMode.PerScope);

        using IServiceScope a = sp.CreateScope();
        using IServiceScope b = sp.CreateScope();

        Assert.Same(a.ServiceProvider.GetRequiredService<MailSettings>(),
            a.ServiceProvider.GetRequiredService<MailSettings>());
        Assert.NotSame(a.ServiceProvider.GetRequiredService<MailSettings>(),
            b.ServiceProvider.GetRequiredService<MailSettings>());
    }

    [Fact]
    public void AddSettings_AlwaysMode_PicksUpConfigurationChange()
    {
        string path = Path.Combine(Path.GetTempPath(), $"typedsettings-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"Mail":{"Host":"one"}}""", Encoding.UTF8);

        try
        {
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false, reloadOnChange: true).Build();
            using ServiceProvider sp = Build(config, SettingsReloadMode.Always);

            Assert.Equal("one", sp.GetRequiredService<MailSettings>().Host);

            File.WriteAllText(path, """{"Mail":{"Host":"two"}}""", Encoding.UTF8);
            SpinWait.SpinUntil(() => config["Mail:Host"] == "two", TimeSpan.FromSeconds(5));
            config.Reload();

            Assert.Equal("two", sp.GetRequiredService<MailSettings>().Host);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetSettings_MissingSection_FallsBackToDefaultsAndValidates()
    {
        IConfiguration config = Config(new Dictionary<string, string?>());

        Assert.Throws<OptionsValidationException>(config.GetSettings<MailSettings>);
    }

    [Fact]
    public void GetSettings_ValidSection_ReturnsBoundValues()
    {
        IConfiguration config = Config(new Dictionary<string, string?> { ["Mail:Host"] = "smtp.test" });

        MailSettings settings = config.GetSettings<MailSettings>();

        Assert.Equal("smtp.test", settings.Host);
        Assert.Equal(25, settings.Port);
    }
}
