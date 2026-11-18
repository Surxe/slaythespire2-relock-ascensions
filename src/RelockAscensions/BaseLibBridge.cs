using System.Reflection;
using System.Runtime.Loader;

namespace RelockAscensions;

/// <summary>
/// Optional BaseLib integration. The mod declares no dependency on BaseLib, so nothing in this
/// assembly may reference a BaseLib type: the game's loader calls <c>GetTypes()</c> on the mod
/// DLL, which would throw if any type here derived from BaseLib's <c>ModConfig</c> and BaseLib
/// were absent. The config class lives in a satellite DLL next to this one, loaded only when
/// BaseLib is already in the process.
/// <para>
/// Registration waits for the first main menu instead of running at mod load: the loader does
/// not order mods without declared dependencies, so BaseLib may load after this mod, but every
/// mod is loaded by the time a menu exists.
/// </para>
/// </summary>
internal static class BaseLibBridge
{
    public const string SatelliteName = ModEntry.ModId + ".BaseLibConfig";

    private const string BaseLibAssembly = "BaseLib";
    private const string RegistrationType = SatelliteName + ".ConfigRegistration";
    private const string RegisterMethod = "Register";

    private static bool _attempted;

    /// <summary>Registers the Mod Configuration page once, if BaseLib is loaded. Never throws.</summary>
    public static void EnsureRegistered()
    {
        if (_attempted)
        {
            return;
        }
        _attempted = true;
        try
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == BaseLibAssembly))
            {
                Log.Info("BaseLib not loaded; settings keep their defaults");
                return;
            }
            var self = typeof(BaseLibBridge).Assembly;
            var path = Path.Combine(Path.GetDirectoryName(self.Location) ?? "", SatelliteName + ".dll");
            if (!File.Exists(path))
            {
                Log.Warn($"BaseLib is loaded but {SatelliteName}.dll is missing; no Mod Configuration page");
                return;
            }
            // Same load context as this mod, so the satellite binds to this assembly and BaseLib.
            var context = AssemblyLoadContext.GetLoadContext(self) ?? AssemblyLoadContext.Default;
            var satellite = context.LoadFromAssemblyPath(path);
            var register = satellite.GetType(RegistrationType, throwOnError: true)!
                .GetMethod(RegisterMethod, BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(RegistrationType, RegisterMethod);
            register.Invoke(null, new object[] { ModEntry.ModId });
            Log.Info($"Mod Configuration page registered with BaseLib. Hide main-menu button: {Settings.HideMainMenuButton}");
        }
        catch (Exception e)
        {
            Log.Error($"BaseLib config registration failed (settings keep their defaults): {e}");
        }
    }
}
