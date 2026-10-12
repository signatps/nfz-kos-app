namespace KosZawal;

public static class AppPaths
{
    public static string DatabaseFile()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KosZawal");
        Directory.CreateDirectory(root);
        return Path.Combine(root, "koszawal.db");
    }
}
