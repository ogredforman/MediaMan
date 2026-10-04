namespace MediaMan.Core;

/// <summary>The permission list from SPEC §8. Roles hold these strings; the server checks them on every endpoint.</summary>
public static class Permissions
{
    public const string LibraryView = "library.view";
    public const string LibraryManage = "library.manage";
    public const string MetadataEdit = "metadata.edit";
    public const string DownloadsRequest = "downloads.request";
    public const string DownloadsCreate = "downloads.create";
    public const string DownloadsApprove = "downloads.approve";
    public const string DownloadsManage = "downloads.manage";
    public const string IndexersManage = "indexers.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string SettingsManage = "settings.manage";
    public const string ServerControl = "server.control";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        LibraryView, LibraryManage, MetadataEdit,
        DownloadsRequest, DownloadsCreate, DownloadsApprove, DownloadsManage,
        IndexersManage, UsersManage, RolesManage, SettingsManage, ServerControl,
    };

    public static bool IsKnown(string? permission) => permission is not null && All.Contains(permission);
}
