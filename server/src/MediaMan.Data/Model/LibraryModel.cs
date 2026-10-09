using MediaMan.Core;
using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Model;

internal static class LibraryModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Library>(b =>
        {
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<MediaFile>(b =>
        {
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).ValueGeneratedNever();
            b.HasIndex(f => f.Path).IsUnique();
            b.HasIndex(f => f.Status);
            b.Property(f => f.Probe).HasJsonConversion();
            b.Property(f => f.Parsed).HasJsonConversion();
            b.HasOne<Library>().WithMany().HasForeignKey(f => f.LibraryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FileLink>(b =>
        {
            b.HasKey(l => new { l.MediaFileId, l.PlayableId });
            b.HasIndex(l => l.PlayableId);
            b.HasOne<MediaFile>().WithMany().HasForeignKey(l => l.MediaFileId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Playable>().WithMany().HasForeignKey(l => l.PlayableId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchReview>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).ValueGeneratedNever();
            b.HasIndex(r => r.MediaFileId).IsUnique();
            b.Property(r => r.Candidates).HasJsonConversion();
            b.HasOne<MediaFile>().WithOne().HasForeignKey<MatchReview>(r => r.MediaFileId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MatchOverride>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Id).ValueGeneratedNever();
            b.HasIndex(o => new { o.LibraryId, o.Path }).IsUnique();
            b.Property(o => o.TitleRef).HasConversion(v => v.ToString(), v => ExternalId.Parse(v));
            b.HasOne<Library>().WithMany().HasForeignKey(o => o.LibraryId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
