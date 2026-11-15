namespace Super.Infrastructure.Persistence;

/// <summary>Ubica <c>appsettings.Local.json</c> (no versionado) subiendo desde una carpeta.</summary>
public static class LocalSettings
{
    public const string FileName = "appsettings.Local.json";

    public static string? FindFile(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, FileName);
            if (File.Exists(file)) return file;
        }
        return null;
    }
}
