using BaseLib.Config;

namespace RelockAscensions.BaseLibConfig;

/// <summary>Entry point the main mod invokes by reflection (<c>BaseLibBridge</c>).</summary>
public static class ConfigRegistration
{
    /// <summary>Loads the saved settings (creating the file with defaults) and lists the page.</summary>
    public static void Register(string modId) => ModConfigRegistry.Register(modId, new RelockConfig());
}
