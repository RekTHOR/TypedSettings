namespace TypedSettings;

/// <summary>
/// How often a settings class is re-read from configuration. The choice also fixes the DI lifetime of the
/// settings object, which in turn limits what may consume it: a singleton service cannot take
/// <see cref="PerScope"/> settings.
/// </summary>
public enum SettingsReloadMode
{
    /// <summary>
    /// Bound once and cached for the lifetime of the process (<c>IOptions</c>, singleton). A configuration
    /// edit only takes effect after a restart. Use for values that must not change while running.
    /// </summary>
    None,

    /// <summary>
    /// Re-read once per DI scope, so once per HTTP request (<c>IOptionsSnapshot</c>, scoped). A configuration
    /// edit reaches the next request, and stays consistent within a request.
    /// </summary>
    PerScope,

    /// <summary>
    /// Re-read on every resolution (<c>IOptionsMonitor.CurrentValue</c>, transient). The most current value,
    /// but two injections inside one request may disagree.
    /// </summary>
    Always
}
