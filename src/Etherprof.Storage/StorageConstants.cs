namespace Etherprof.Storage;

public static class StorageConstants
{
    public static string BasePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Etherprof");

    public static string SettingsFile => Path.Combine(BasePath, "settings.json");
    public static string ProfilesFile => Path.Combine(BasePath, "profiles.json");
    public static string TestsFile => Path.Combine(BasePath, "tests.json");
    public static string WifiBssidsFile => Path.Combine(BasePath, "wifi-bssids.json");
    public static string LogsDirectory => Path.Combine(BasePath, "logs");

    public const int CurrentSettingsSchemaVersion = 1;
    public const int CurrentProfilesSchemaVersion = 1;
    public const int CurrentTestsSchemaVersion = 1;
    public const int CurrentWifiBssidsSchemaVersion = 1;
}
