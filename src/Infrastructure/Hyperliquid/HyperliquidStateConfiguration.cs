using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Hyperliquid;

/// <summary>Durable source state, distinct-address reservations and owner-specific recipient work.</summary>
public sealed class HyperliquidStateConfiguration : IEntityTypeConfiguration<HyperliquidSetting>,
    IEntityTypeConfiguration<HyperliquidReservation>, IEntityTypeConfiguration<HyperliquidEvent>,
    IEntityTypeConfiguration<HyperliquidCheckpoint>, IEntityTypeConfiguration<HyperliquidDelivery>
{
    public void Configure(EntityTypeBuilder<HyperliquidSetting> b)
    {
        b.HasKey(x => x.WalletId);
        b.HasOne<TrackedWallet>().WithOne().HasForeignKey<HyperliquidSetting>(x => x.WalletId).OnDelete(DeleteBehavior.Cascade);
    }
    public void Configure(EntityTypeBuilder<HyperliquidReservation> b) { b.HasKey(x => x.Address); b.Property(x => x.Address).HasMaxLength(42); }
    public void Configure(EntityTypeBuilder<HyperliquidEvent> b) { b.HasKey(x => x.Id); b.HasIndex(x => x.StoredAt); b.HasIndex(x => new { x.Address, x.Stream, x.Time }); }
    public void Configure(EntityTypeBuilder<HyperliquidCheckpoint> b) { b.HasKey(x => x.Key); }
    public void Configure(EntityTypeBuilder<HyperliquidDelivery> b)
    {
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EventId, x.WalletId, x.Generation }).IsUnique();
        b.HasIndex(x => new { x.State, x.DueAt });
        b.HasOne<HyperliquidEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<TrackedWallet>().WithMany().HasForeignKey(x => x.WalletId).OnDelete(DeleteBehavior.Cascade);
    }
}
