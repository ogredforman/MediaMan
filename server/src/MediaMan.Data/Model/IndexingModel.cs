using MediaMan.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaMan.Data.Model;

internal static class IndexingModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndexerDefinition>(b =>
        {
            b.HasKey(d => d.Id);
            b.Property(d => d.Id).ValueGeneratedNever();
            b.HasIndex(d => d.Key).IsUnique();
        });

        modelBuilder.Entity<Indexer>(b =>
        {
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).ValueGeneratedNever();
            b.Property(i => i.Settings).HasJsonConversion();
            b.Property(i => i.Health).HasJsonConversion();
            b.HasOne<IndexerDefinition>().WithMany().HasForeignKey(i => i.DefinitionId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
