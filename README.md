# RekTHOR.TypedSettings

Strongly typed, validated settings on top of the .NET Options pattern.

- The settings type names its own configuration section.
- DI hands out the plain settings object, not `IOptions<T>`.
- An enum picks the reload behaviour (and with it the DI lifetime).
- DataAnnotations are validated on startup.

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

## Reload modes

| Mode       | Backed by                         | Lifetime  | Change takes effect       |
|------------|-----------------------------------|-----------|---------------------------|
| `None`     | `IOptions<T>`                     | Singleton | after restart             |
| `PerScope` | `IOptionsSnapshot<T>`             | Scoped    | next scope / HTTP request |
| `Always`   | `IOptionsMonitor<T>.CurrentValue` | Transient | on every resolution       |

A singleton service cannot consume `PerScope` settings.

## License

MIT
