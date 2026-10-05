using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MediaMan.Data.Model;

internal static class AccountsModel
{
    public static void Configure(ModelBuilder modelBuilder, ISecretProtector secrets)
    {
        modelBuilder.Entity<Role>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.HasIndex(r => r.Name).IsUnique();
            b.Property(r => r.Name).UseCollation("NOCASE");
        });

        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Id).ValueGeneratedNever();
            b.Property(u => u.Username).UseCollation("NOCASE");
            b.HasIndex(u => u.Username).IsUnique();
            b.Property(u => u.TotpSecret).HasConversion((ValueConverter)new SecretConverter(secrets));

            // At most one Superadmin, enforced by the database (SPEC §5, §7.11).
            b.HasIndex(u => u.IsSuperadmin).IsUnique().HasFilter("\"IsSuperadmin\" = 1");
            b.HasOne<Role>().WithMany().HasForeignKey(u => u.RoleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Profile>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Session>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.HasIndex(s => s.RefreshTokenHash).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invite>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).ValueGeneratedNever();
            b.HasIndex(i => i.TokenHash).IsUnique();
            b.HasOne<Role>().WithMany().HasForeignKey(i => i.RoleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(i => i.CreatedBy).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WatchState>(b =>
        {
            b.HasKey(w => new { w.ProfileId, w.PlayableId });
            b.HasIndex(w => new { w.ProfileId, w.LastWatchedAt });
            b.HasOne<Profile>().WithMany().HasForeignKey(w => w.ProfileId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Playable>().WithMany().HasForeignKey(w => w.PlayableId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasIndex(e => e.At);
            b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
