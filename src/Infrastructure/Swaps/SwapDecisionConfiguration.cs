using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Swaps;

/// <summary>Unique logical identities survive reconnect/restart for the retention horizon.</summary>
public sealed class SwapDecisionConfiguration : IEntityTypeConfiguration<SwapDecision>
{
    public void Configure(EntityTypeBuilder<SwapDecision> b)
    {
        b.ToTable("SwapDecisions"); b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Chain, x.TransactionHash, x.UserId, x.WalletId }).IsUnique();
        b.HasIndex(x => new { x.State, x.LeaseUntil }); b.HasIndex(x => x.ExpiresAt);
        b.Property(x => x.TransactionHash).HasMaxLength(66); b.Property(x => x.BlockHash).HasMaxLength(66);
        b.Property(x => x.WalletAddress).HasMaxLength(42); b.Property(x => x.State).HasMaxLength(32);
        b.Property(x => x.Reason).HasMaxLength(100);
        b.Property(x => x.BlockNumber).HasConversion<long>();
    }
}
