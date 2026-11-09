using System;

namespace pcb_deftek.Common.Exceptions;

/// <summary>
/// Exception thrown when application settings or configuration values are corrupted, missing, or invalid.
/// </summary>
public class ConfigurationException : PcbDeftekException
{
    /// <summary>
    /// Gets the configuration key or setting name that failed, if applicable.
    /// </summary>
    public string? SettingKey { get; }

    public ConfigurationException(string message, string? settingKey = null, Exception? innerException = null)
        : base(message, innerException)
    {
        SettingKey = settingKey;
    }
}
