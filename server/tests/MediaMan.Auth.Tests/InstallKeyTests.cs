namespace MediaMan.Auth.Tests;

public sealed class InstallKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "mediaman-tests", Guid.NewGuid().ToString("N"));

    private string KeyPath => Path.Combine(_directory, "data", "mediaman.key");

    [Fact]
    public void First_run_creates_a_random_key_file_and_later_runs_reuse_it()
    {
        var first = InstallKey.LoadOrCreate(KeyPath);
        var bytes = File.ReadAllBytes(KeyPath);
        var second = InstallKey.LoadOrCreate(KeyPath);

        bytes.Should().HaveCount(InstallKey.SizeBytes);
        bytes.Should().NotEqual(new byte[InstallKey.SizeBytes]);
        File.ReadAllBytes(KeyPath).Should().Equal(bytes);
        second.Derive(InstallKey.SecretsAtRest).Should().Equal(first.Derive(InstallKey.SecretsAtRest));
        Directory.GetFiles(Path.GetDirectoryName(KeyPath)!).Should().ContainSingle("no temp file is left behind");
    }

    [Fact]
    public void Each_install_gets_its_own_key()
    {
        var a = InstallKey.LoadOrCreate(KeyPath);
        var b = InstallKey.LoadOrCreate(Path.Combine(_directory, "other", "mediaman.key"));

        a.Derive(InstallKey.SecretsAtRest).Should().NotEqual(b.Derive(InstallKey.SecretsAtRest));
    }

    [Fact]
    public void Subkeys_differ_by_purpose_and_never_equal_the_key()
    {
        var raw = Enumerable.Range(0, InstallKey.SizeBytes).Select(i => (byte)i).ToArray();
        var key = InstallKey.FromBytes(raw);

        var secrets = key.Derive(InstallKey.SecretsAtRest);
        var other = key.Derive("access-tokens");

        secrets.Should().HaveCount(32);
        secrets.Should().NotEqual(other);
        secrets.Should().NotEqual(raw);
        key.Derive(InstallKey.SecretsAtRest).Should().Equal(secrets, "derivation is deterministic");
    }

    [Fact]
    public void A_damaged_key_file_is_reported_and_never_replaced()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllBytes(KeyPath, [1, 2, 3]);

        var act = () => InstallKey.LoadOrCreate(KeyPath);

        act.Should().Throw<InvalidOperationException>().WithMessage("*damaged*Restore it from the backup*");
        File.ReadAllBytes(KeyPath).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void A_key_of_the_wrong_size_is_rejected()
    {
        var act = () => InstallKey.FromBytes(new byte[16]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_key_file_is_owner_only_on_unix()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes don't apply on Windows; the data directory's per-user ACL covers it.");
            return;
        }

        InstallKey.LoadOrCreate(KeyPath);

        File.GetUnixFileMode(KeyPath).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
