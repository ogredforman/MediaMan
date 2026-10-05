namespace MediaMan.Server.Hosting;

/// <summary>Server configuration, bound from the <c>MediaMan</c> section.</summary>
public sealed class MediaManOptions
{
    public const string SectionName = "MediaMan";
    public const string DatabaseFileName = "mediaman.db";

    /// <summary>Holds the database, the per-install key file and the artwork cache (SPEC §9 data portability).</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MediaMan");

    public string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);
}
