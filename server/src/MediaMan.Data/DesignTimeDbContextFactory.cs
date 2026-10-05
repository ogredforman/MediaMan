using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore.Design;

namespace MediaMan.Data;

/// <summary>Used only by <c>dotnet ef</c> to build the model for migrations; never opens a real database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MediaManDbContext>
{
    public MediaManDbContext CreateDbContext(string[] args) =>
        new(
            MediaManDb.CreateOptions(Path.Combine(Path.GetTempPath(), "mediaman-design-time.db")),
            new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(AesGcmSecretProtector.KeySizeBytes)));
}
