namespace UsbDaq.App.Services;

public static class AppVersion
{
    // Reads from assembly — set by <Version> in .csproj
    public static readonly string Current =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
