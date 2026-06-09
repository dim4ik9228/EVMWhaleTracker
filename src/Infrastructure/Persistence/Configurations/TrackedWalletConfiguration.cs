using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class TrackedWalletConfiguration : IEntityTypeConfiguration<TrackedWallet>
{
    public void Configure(EntityTypeBuilder<TrackedWallet> builder)
    {
        builder.ToTable("TrackedWallets");

        builder.HasKey(wallet => wallet.Id);

        builder.Property(wallet => wallet.Address)
            .HasConversion(
                address => address.Value,
                value => Address.Normalize(value).Value)
            .HasMaxLength(42)
            .IsRequired();

        builder.Property(wallet => wallet.UserId).IsRequired();
        builder.Property(wallet => wallet.MinimumTransactionValue).HasPrecision(38, 18);
        builder.Property(wallet => wallet.Alias)
            .HasMaxLength(100);
        builder.Property(wallet => wallet.TrackIncomingTransfers).IsRequired();
        builder.Property(wallet => wallet.TrackOutgoingTransfers).IsRequired();
        builder.Property(wallet => wallet.TrackTokenTransfers).IsRequired();
        builder.Ignore(wallet => wallet.EnabledChains);
        builder.Property<string>("_enabledChainsJson")
            .HasColumnName("EnabledChains")
            .HasDefaultValue("[]")
            .IsRequired();
        builder.Property(wallet => wallet.CreatedAtUtc).IsRequired();
        builder.Property(wallet => wallet.UpdatedAtUtc);

        builder.HasIndex(wallet => new { wallet.UserId, wallet.Address })
            .IsUnique();
    }
}
