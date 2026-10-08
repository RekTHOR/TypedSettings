namespace TypedSettings;

/// <summary>
/// Marks a class as a strongly typed settings object that names its own configuration section.
/// </summary>
/// <typeparam name="TSelf">The implementing settings type itself (curiously recurring pattern).</typeparam>
public interface ISettings<TSelf> where TSelf : class, ISettings<TSelf>
{
    /// <summary>
    /// The name of the configuration section the settings are bound from (e.g. <c>"Mail"</c>).
    /// </summary>
    static abstract string SectionName { get; }
}
