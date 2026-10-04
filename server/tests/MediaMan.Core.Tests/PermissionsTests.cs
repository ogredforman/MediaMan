namespace MediaMan.Core.Tests;

public class PermissionsTests
{
    [Fact]
    public void All_holds_exactly_the_SPEC_section_8_permissions()
    {
        Permissions.All.Should().BeEquivalentTo(
            "library.view", "library.manage", "metadata.edit",
            "downloads.request", "downloads.create", "downloads.approve", "downloads.manage",
            "indexers.manage", "users.manage", "roles.manage", "settings.manage", "server.control");
    }

    [Theory]
    [InlineData("library.view", true)]
    [InlineData("server.control", true)]
    [InlineData("Library.View", false)]
    [InlineData("library.delete", false)]
    [InlineData(null, false)]
    public void IsKnown_matches_exact_names_only(string? permission, bool expected)
    {
        Permissions.IsKnown(permission).Should().Be(expected);
    }
}
