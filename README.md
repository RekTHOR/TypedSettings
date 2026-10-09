# RekTHOR.TypedSettings

Strongly typed, validated settings on top of the .NET Options pattern.

- The settings type names its own configuration section.
- DI hands out the plain settings object, not `IOptions<T>`.
- An enum picks the reload behaviour (and with it the DI lifetime).
- DataAnnotations are validated on startup, recursively: nested objects and collection items too.

## Usage

```csharp
public sealed class MailSettings : ISettings<MailSettings>
{
    public static string SectionName => "Mail";

    [Required] public string Host { get; set; } = "";
    [Range(1, 65535)] public int Port { get; set; } = 25;
}

builder.Services.AddSettings<MailSettings>();                          // bound once, singleton
builder.Services.AddSettings<MailSettings>(SettingsReloadMode.PerScope); // re-read per request

public class Mailer(MailSettings settings) { }
```

Before the container exists (e.g. while configuring the host) use the validated one-off read:

```csharp
MailSettings mail = builder.Configuration.GetSettings<MailSettings>();
```

## Validation

DataAnnotations are validated **recursively** on both paths, with the same rules:

- `AddSettings<T>()` validates on startup (`ValidateOnStart`) and fails the host before it serves anything.
- `GetSettings<T>()` validates the one-off read and throws an `OptionsValidationException`.

```csharp
public sealed class PdfSettings : ISettings<PdfSettings>
{
    public static string SectionName => "Pdf";

    [Required] public Section Confirmation { get; set; } = null!;
    public List<Clause> Clauses { get; set; } = [];

    public sealed class Section
    {
        [Required] public string Title { get; set; } = null!;
        [MinLength(1)] public string[] Paragraphs { get; set; } = [];
    }

    public sealed class Clause
    {
        [Required] public string Text { get; set; } = null!;
    }
}
```

No extra attribute is needed on `Confirmation` or `Clauses`: the properties of nested objects and of the items of any
collection are checked too, to any depth. A failure names the configuration path it came from:

```
Pdf:Confirmation: The Title field is required.
Pdf:Clauses:1: The Text field is required.
```

`IValidatableObject` is honoured on every level. A `[Required]` on a nested object itself only checks that it is not
`null`;
the rules inside it are what the recursion adds.

Not inspected: non-public properties (they are not bound either), and types in the `System` namespaces (`TimeSpan`,
`Uri`, `Dictionary<,>`, ...). Reference cycles are safe.

### Validating before `app.Run()`

`AddSettings<T>()` validates on startup, but that check only runs when the host starts, i.e. at `app.Run()`. Anything that
uses the settings earlier, for example a database migration, would run with a bad configuration first. To be sure,
trigger the validation yourself right after `Build()`:

```csharp
WebApplication app = builder.Build();

app.Services.GetRequiredService<IStartupValidator>().Validate();   // throws OptionsValidationException

using (IServiceScope scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
}

app.Run();
```

`IStartupValidator` (from `Microsoft.Extensions.Options`) runs the validation of every settings type registered with
`AddSettings<T>()` at once, and it is the same recursive validation: nested objects and collection items are included,
and the exception lists every failure. No extra TypedSettings API is needed for this.

Settings needed while the services are still being registered (a connection string for `AddDbContext`, the JWT key for
authentication) cannot come from DI yet. Read them with `GetSettings<T>()`, which validates recursively as well:

```csharp
DatabaseSettings database = builder.Configuration.GetSettings<DatabaseSettings>();

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseMySQL(database.DefaultConnection));
builder.Services.AddSettings<DatabaseSettings>();
```

### Why a custom validator

The stock pieces do not recurse on their own:

- `ValidateDataAnnotations()` (`Microsoft.Extensions.Options.DataAnnotations`) only enters nested objects that carry
  `[ValidateObjectMembers]` and collections that carry `[ValidateEnumeratedItems]`. Without them the rules inside are
  silently ignored.
- `Validator.TryValidateObject(..., validateAllProperties: true)`, which the one-off `GetSettings<T>()` read used to
  call by hand, never enters nested objects at all, so `[ValidateObjectMembers]` did not help there either.

Both are replaced by one internal recursive validator, so the startup check and the one-off read can no longer disagree.

### Upgrading from 0.1.x

- Attributes inside nested objects and collection items that were silently ignored before **now take effect**. Settings
  that
  started fine on 0.1.x may fail on startup with 0.2.0 until the configuration is complete. Check every environment's
  configuration before rolling out.
- `[ValidateObjectMembers]` and `[ValidateEnumeratedItems]` are no longer needed. They are harmless if left in place.
- If your own code validated a settings object with `Validator.TryValidateObject`, it still does not recurse. Read the
  settings through `GetSettings<T>()` or inject them instead, or write the same walk yourself.
- The `Microsoft.Extensions.Options.DataAnnotations` package is no longer a dependency.

## Reload modes

| Mode       | Backed by                         | Lifetime  | Change takes effect       |
|------------|-----------------------------------|-----------|---------------------------|
| `None`     | `IOptions<T>`                     | Singleton | after restart             |
| `PerScope` | `IOptionsSnapshot<T>`             | Scoped    | next scope / HTTP request |
| `Always`   | `IOptionsMonitor<T>.CurrentValue` | Transient | on every resolution       |

A singleton service cannot consume `PerScope` settings.

## License

MIT
