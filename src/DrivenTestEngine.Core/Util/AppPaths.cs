namespace DrivenTestEngine.Core.Util;

/// <summary>Well-known folders used by the engine, all under the current user's LocalAppData.</summary>
public static class AppPaths
{
    public const string ProductFolder = "DrivenTestEngine";

    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolder);

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    public static string HistoryFile => Path.Combine(Root, "history.json");

    public static string LogFolder => Path.Combine(Root, "logs");

    public static void EnsureCreated()
    {
        try
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(LogFolder);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
