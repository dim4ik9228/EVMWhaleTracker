using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Billing;

/// <summary>Ledger uniqueness is enforced by PostgreSQL, independently of callback arrival order.</summary>
public sealed class BillingStateConfiguration : IEntityTypeConfiguration<BillingAccount>, IEntityTypeConfiguration<PurchaseOrder>,
    IEntityTypeConfiguration<PaymentAttempt>, IEntityTypeConfiguration<PaymentReceipt>, IEntityTypeConfiguration<AccessGrant>,
    IEntityTypeConfiguration<BillingOutbox>, IEntityTypeConfiguration<BillingCheckpoint>, IEntityTypeConfiguration<BillingAudit>, IEntityTypeConfiguration<WalletAccessState>
{
    public void Configure(EntityTypeBuilder<WalletAccessState> b) { b.HasKey(x => x.WalletId); b.HasOne<TrackedWallet>().WithOne().HasForeignKey<WalletAccessState>(x => x.WalletId).OnDelete(DeleteBehavior.Cascade); }
    public void Configure(EntityTypeBuilder<BillingAccount> b) { b.HasKey(x => x.UserId); b.Property(x => x.Revision).IsConcurrencyToken(); }
    public void Configure(EntityTypeBuilder<PurchaseOrder> b) { b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.CreatedAt }); b.Property(x => x.UsdPrice).HasPrecision(28, 12); }
    public void Configure(EntityTypeBuilder<PaymentAttempt> b)
    {
        b.HasKey(x => x.Id); b.HasIndex(x => new { x.OrderId, x.Provider }).IsUnique();
        b.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique(); b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.OrderId);
        b.Property(x => x.ExpectedAmount).HasPrecision(38, 18); b.Property(x => x.PaidAmount).HasPrecision(38, 18);
    }
    public void Configure(EntityTypeBuilder<PaymentReceipt> b) { b.HasKey(x => x.Id); b.HasIndex(x => new { x.Provider, x.EventKey }).IsUnique(); b.HasIndex(x => new { x.Provider, x.ChargeId }).IsUnique().HasFilter("\"State\" = 'Applied'"); b.Property(x => x.Amount).HasPrecision(38, 18); }
    public void Configure(EntityTypeBuilder<AccessGrant> b) { b.HasKey(x => x.Id); b.HasIndex(x => x.OrderId).IsUnique(); b.HasIndex(x => new { x.UserId, x.EndsAt }); }
    public void Configure(EntityTypeBuilder<BillingOutbox> b) { b.HasKey(x => x.Key); b.HasIndex(x => new { x.SentAt, x.RetryAt }); }
    public void Configure(EntityTypeBuilder<BillingCheckpoint> b) => b.HasKey(x => x.Key);
    public void Configure(EntityTypeBuilder<BillingAudit> b) { b.HasKey(x => x.Id); b.HasIndex(x => new { x.UserId, x.At }); }
}
