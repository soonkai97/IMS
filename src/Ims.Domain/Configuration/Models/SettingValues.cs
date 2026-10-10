namespace Ims.Domain;

/// <summary>How boolean settings are stored in the key/value table.</summary>
public static class SettingValues
{
    public const string True = "true";
    public const string False = "false";

    public static string From(bool value) => value ? True : False;
    public static bool IsTrue(string? value) => value == True;
}
