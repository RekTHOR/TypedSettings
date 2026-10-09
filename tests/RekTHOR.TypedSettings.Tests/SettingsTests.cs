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

    private sealed class PdfSettings : ISettings<PdfSettings>
    {
        public static string SectionName => "Pdf";

        [Required] public Section Confirmation { get; set; } = null!;

        public List<Clause> Clauses { get; set; } = [];

        public sealed class Section
        {
            [Required] 
            public string Title { get; set; } = null!;

            [MinLength(1)] 
            public string[] Paragraphs { get; set; } = [];

            public Section? Child { get; set; }
        }

        public sealed class Clause
        {
            [Required] 
            public string Text { get; set; } = null!;
        }
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

    private static Dictionary<string, string?> ValidPdf() => new()
    {
        ["Pdf:Confirmation:Title"] = "t",
        ["Pdf:Confirmation:Paragraphs:0"] = "p"
    };

    [Fact]
    public void GetSettings_NestedObjectInvalid_ReportsConfigurationPath()
    {
        Dictionary<string, string?> values = ValidPdf();
        values.Remove("Pdf:Confirmation:Title");

        OptionsValidationException ex =
            Assert.Throws<OptionsValidationException>(Config(values).GetSettings<PdfSettings>);

        string failure = Assert.Single(ex.Failures);
        Assert.StartsWith("Pdf:Confirmation: ", failure);
        Assert.Contains("Title", failure);
    }

    [Fact]
    public void GetSettings_NestedArrayTooShort_Fails()
    {
        Dictionary<string, string?> values = ValidPdf();
        values.Remove("Pdf:Confirmation:Paragraphs:0");

        Assert.Throws<OptionsValidationException>(Config(values).GetSettings<PdfSettings>);
    }

    [Fact]
    public void GetSettings_CollectionItemInvalid_ReportsIndex()
    {
        Dictionary<string, string?> values = ValidPdf();
        values["Pdf:Clauses:1:Text"] = "";
        values["Pdf:Clauses:0:Text"] = "ok";

        OptionsValidationException ex =
            Assert.Throws<OptionsValidationException>(Config(values).GetSettings<PdfSettings>);

        Assert.StartsWith("Pdf:Clauses:1: ", Assert.Single(ex.Failures));
    }

    [Fact]
    public void GetSettings_DeeplyNestedInvalid_Fails()
    {
        Dictionary<string, string?> values = ValidPdf();
        values["Pdf:Confirmation:Child:Paragraphs:0"] = "p";

        OptionsValidationException ex =
            Assert.Throws<OptionsValidationException>(Config(values).GetSettings<PdfSettings>);

        Assert.StartsWith("Pdf:Confirmation:Child: ", Assert.Single(ex.Failures));
    }

    [Fact]
    public void GetSettings_NestedValid_Succeeds()
    {
        PdfSettings settings = Config(ValidPdf()).GetSettings<PdfSettings>();

        Assert.Equal("t", settings.Confirmation.Title);
    }

    [Fact]
    public void AddSettings_NestedObjectInvalid_FailsOnResolve()
    {
        Dictionary<string, string?> values = ValidPdf();
        values.Remove("Pdf:Confirmation:Title");

        ServiceCollection services = new();
        services.AddSingleton(Config(values));
        services.AddSettings<PdfSettings>();
        using ServiceProvider sp = services.BuildServiceProvider();

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(sp.GetRequiredService<PdfSettings>);

        Assert.Single(ex.Failures);
    }

    [Fact]
    public void StartupValidator_NestedObjectInvalid_FailsBeforeAnySettingsIsResolved()
    {
        Dictionary<string, string?> values = ValidPdf();
        values.Remove("Pdf:Confirmation:Title");

        ServiceCollection services = new();
        services.AddSingleton(Config(values));
        services.AddSettings<PdfSettings>();
        using ServiceProvider sp = services.BuildServiceProvider();

        OptionsValidationException ex =
            Assert.Throws<OptionsValidationException>(sp.GetRequiredService<IStartupValidator>().Validate);

        Assert.StartsWith("Pdf:Confirmation: ", Assert.Single(ex.Failures));
    }

    [Fact]
    public void StartupValidator_ValidSettings_Passes()
    {
        ServiceCollection services = new();
        services.AddSingleton(Config(ValidPdf()));
        services.AddSettings<PdfSettings>();
        using ServiceProvider sp = services.BuildServiceProvider();

        sp.GetRequiredService<IStartupValidator>().Validate();
    }
}
