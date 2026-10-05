using MediaMan.Core;
using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace MediaMan.Data.Model;

internal static class DownloadsModel
{
    /// <summary>VpnConfig is a single row; its shadow key is always this value (ADR 0003).</summary>
    public const string VpnConfigRowId = "vpn";

    public static void Configure(ModelBuilder modelBuilder, ISecretProtector secrets)
    {
        modelBuilder.Entity<Download>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.Property(d => d.InfoHash).HasConversion(v => v.Hex, v => InfoHash.Parse(v));
            b.HasIndex(d => d.InfoHash).IsUnique();
            b.HasOne<Title>().WithMany().HasForeignKey(d => d.TargetTitleId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<User>().WithMany().HasForeignKey(d => d.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DownloadFile>(b =>
        {
            b.HasKey(f => new { f.DownloadId, f.Index });
            b.HasOne<Download>().WithMany().HasForeignKey(f => f.DownloadId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadRequest>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.Property(r => r.TitleRef).HasConversion(v => v.ToString(), v => ExternalId.Parse(v));
            b.Property(r => r.Release).HasJsonConversion();
            b.Property(r => r.Filter).HasJsonConversion();
            b.HasIndex(r => r.Status);
            b.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<User>().WithMany().HasForeignKey(r => r.DecidedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SavedQualityProfile>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.Property(p => p.Filter).HasJsonConversion();
            b.HasIndex(p => new { p.UserId, p.Name }).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VpnConfig>(b =>
        {
            b.ToTable(t => t.HasCheckConstraint("CK_VpnConfig_SingleRow", $"\"Id\" = '{VpnConfigRowId}'"));
            b.Property<string>("Id")
                .ValueGeneratedOnAdd()
                .HasValueGenerator<VpnConfigIdGenerator>();
            b.HasKey("Id");
            b.Property(v => v.SocksUsername).HasConversion((ValueConverter)new SecretConverter(secrets));
            b.Property(v => v.SocksPassword).HasConversion((ValueConverter)new SecretConverter(secrets));
        });

        modelBuilder.Entity<Setting>(b => b.HasKey(s => s.Key));
    }
}

/// <summary>Stamps the singleton VpnConfig row's shadow key on add.</summary>
internal sealed class VpnConfigIdGenerator : ValueGenerator<string>
{
    public override bool GeneratesTemporaryValues => false;

    public override string Next(EntityEntry entry) => DownloadsModel.VpnConfigRowId;
}
