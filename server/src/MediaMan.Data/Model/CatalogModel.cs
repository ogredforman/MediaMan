using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Model;

internal static class CatalogModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Title>(b =>
        {
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.OwnsOne(t => t.ExternalIds, o =>
            {
                o.HasIndex(x => x.Tmdb);
                o.HasIndex(x => x.Imdb);
                o.HasIndex(x => x.Tvdb);
                o.HasIndex(x => x.AniList);
            });
            b.Navigation(t => t.ExternalIds).IsRequired();
        });

        modelBuilder.Entity<Season>(b =>
        {
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).ValueGeneratedNever();
            b.HasIndex(s => new { s.TitleId, s.Number }).IsUnique();
            b.HasOne<Title>().WithMany().HasForeignKey(s => s.TitleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Episode>(b =>
        {
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.HasIndex(e => new { e.SeasonId, e.Number }).IsUnique();
            b.HasOne<Season>().WithMany().HasForeignKey(e => e.SeasonId).OnDelete(DeleteBehavior.Cascade);
        });

        // ADR 0002: exactly one FK set, matching Kind.
        modelBuilder.Entity<Playable>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).ValueGeneratedNever();
            b.HasIndex(p => p.TitleId).IsUnique();
            b.HasIndex(p => p.EpisodeId).IsUnique();
            b.HasOne<Title>().WithMany().HasForeignKey(p => p.TitleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Episode>().WithMany().HasForeignKey(p => p.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => t.HasCheckConstraint(
                "CK_Playables_KindMatchesOwner",
                $"""("Kind" = '{PlayableKind.Movie}' AND "TitleId" IS NOT NULL AND "EpisodeId" IS NULL) OR ("Kind" = '{PlayableKind.Episode}' AND "EpisodeId" IS NOT NULL AND "TitleId" IS NULL)"""));
        });

        // ADR 0002: exactly one owner FK.
        modelBuilder.Entity<Artwork>(b =>
        {
            b.HasKey(a => a.Id);
            b.Property(a => a.Id).ValueGeneratedNever();
            b.HasOne<Title>().WithMany().HasForeignKey(a => a.TitleId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Season>().WithMany().HasForeignKey(a => a.SeasonId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<Episode>().WithMany().HasForeignKey(a => a.EpisodeId).OnDelete(DeleteBehavior.Cascade);
            b.ToTable(t => t.HasCheckConstraint(
                "CK_Artwork_ExactlyOneOwner",
                """("TitleId" IS NOT NULL) + ("SeasonId" IS NOT NULL) + ("EpisodeId" IS NOT NULL) = 1"""));
        });
    }
}
