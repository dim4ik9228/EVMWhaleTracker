using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Polymarket;

/// <summary>Independent protocol persistence with owner cascade and durable inbox retention.</summary>
public sealed class PolymarketStateConfiguration : IEntityTypeConfiguration<PolymarketSetting>,
    IEntityTypeConfiguration<PolymarketEvent>, IEntityTypeConfiguration<PolymarketCheckpoint>, IEntityTypeConfiguration<PolymarketDelivery>,
    IEntityTypeConfiguration<PolymarketRawLog>, IEntityTypeConfiguration<PolymarketBlock>
{
    public void Configure(EntityTypeBuilder<PolymarketRawLog> b)
    { b.HasKey(x => x.Id); b.HasIndex(x => new { x.DecodeState, x.BlockNumber }); b.HasIndex(x => x.StoredAt); }
    public void Configure(EntityTypeBuilder<PolymarketBlock> b) { b.HasKey(x => x.Number); }
    public void Configure(EntityTypeBuilder<PolymarketSetting> b)
    {
        b.HasKey(x => x.WalletId);
        b.Property(x => x.SourceAddress).HasMaxLength(42);
        b.Property(x => x.ProposedAddress).HasMaxLength(42);
        b.HasIndex(x => new { x.Enabled, x.SourceAddress });
        b.HasOne<TrackedWallet>().WithOne().HasForeignKey<PolymarketSetting>(x => x.WalletId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<PolymarketEvent> b)
    {
        b.HasKey(x => x.Id); b.HasIndex(x => x.StoredAt); b.HasIndex(x => new { x.SourceAddress, x.Time });
    }
    public void Configure(EntityTypeBuilder<PolymarketCheckpoint> b) { b.HasKey(x => x.SourceAddress); }
    public void Configure(EntityTypeBuilder<PolymarketDelivery> b)
    {
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.EventId, x.WalletId, x.Generation }).IsUnique();
        b.HasIndex(x => new { x.State, x.DueAt });
        b.HasOne<PolymarketEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TrackedWallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Cascade);
    }
}
