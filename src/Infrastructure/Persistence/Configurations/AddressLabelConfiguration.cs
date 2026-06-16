using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

public sealed class AddressLabelConfiguration : IEntityTypeConfiguration<AddressLabel>
{
    public void Configure(EntityTypeBuilder<AddressLabel> builder)
    {
        builder.ToTable("AddressLabels");

        builder.HasKey(label => label.Id);

        builder.Property(label => label.Address)
            .HasConversion(
                address => address.Value,
                value => Address.Normalize(value).Value)
            .HasMaxLength(42)
            .IsRequired();

        builder.Property(label => label.Chain)
            .HasConversion<int?>()
            .IsRequired(false);

        builder.Property(label => label.Label)
            .HasMaxLength(AddressLabel.MaxLabelLength)
            .IsRequired();

        builder.Property(label => label.Kind)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(label => label.Source)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(label => label.SourceReference)
            .HasMaxLength(AddressLabel.MaxSourceReferenceLength);

        builder.Property(label => label.Priority).IsRequired();
        builder.Property(label => label.IsEnabled).IsRequired();
        builder.Property(label => label.CreatedAtUtc).IsRequired();
        builder.Property(label => label.UpdatedAtUtc).IsRequired();

        builder.HasIndex(label => label.Chain);
        builder.HasIndex(label => label.Kind);
        builder.HasIndex(label => label.Source);

        builder.HasIndex(label => new { label.Address, label.Chain })
            .IsUnique()
            .HasFilter("\"IsEnabled\" = TRUE AND \"Chain\" IS NOT NULL");

        builder.HasIndex(label => label.Address)
            .IsUnique()
            .HasDatabaseName("IX_AddressLabels_Address_Global")
            .HasFilter("\"IsEnabled\" = TRUE AND \"Chain\" IS NULL");
    }
}
