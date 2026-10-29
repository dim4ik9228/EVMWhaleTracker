using System.Text.Json;
using Application.Abstractions;
using Application.Billing;
using Application.Polymarket;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Hyperliquid;
using Infrastructure.Persistence;
using Infrastructure.Polymarket;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Billing;

/// <summary>Owner-serialized ledger and projections. Lock order is owner, HL admission, PM admission.
/// Provider checkout HTTP never runs inside these transactions.</summary>
public sealed class BillingService(AppDbContext db, IOptions<BillingOptions> options, TimeProvider clock,
    EntitlementPolicy policy, ITrackedWalletCacheService cache, HyperliquidChangeSignal signal,
    IOptions<HyperliquidOptions> hlOptions, IOptions<PolymarketPolygonOptions> pmOptions,
    IPolymarketPolygonRpc? rpc = null, IPolymarketProfileResolver? profiles = null) : IBillingService
{
    public static Task LockOwnerAsync(AppDbContext db, long owner, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({unchecked(owner ^ long.MinValue)})", ct);

    private async Task<BillingAccount> AccountAsync(long owner, CancellationToken ct)
    {
        var a = await db.Set<BillingAccount>().SingleOrDefaultAsync(x => x.UserId == owner, ct);
        if (a is null) { a = new() { UserId = owner }; db.Add(a); }
        else await db.Entry(a).ReloadAsync(ct);
        var wallets = await db.TrackedWallets.AsNoTracking().Where(x => x.UserId == owner).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = JsonSerializer.Deserialize<List<Guid>>(a.FreeWalletIds) ?? [];
        ids.RemoveAll(id => wallets.All(w => w.Id != id));
        if (!a.SelectionInitialized)
        {
            ids = wallets.Take(BillingPlans.FreeWallets).Select(x => x.Id).ToList();
            a.FreePolymarketWalletId = await db.Set<PolymarketSetting>().Where(x => ids.Contains(x.WalletId) && x.Enabled)
                .OrderBy(x => x.ActivatedAt).ThenBy(x => x.WalletId).Select(x => (Guid?)x.WalletId).FirstOrDefaultAsync(ct);
            a.SelectionInitialized = true;
        }
        if (a.FreePolymarketWalletId is { } pm && !ids.Contains(pm)) a.FreePolymarketWalletId = null;
        a.FreeWalletIds = JsonSerializer.Serialize(ids);
        return a;
    }
    private Task<List<AccessGrant>> GrantsAsync(long owner, CancellationToken ct) => db.Set<AccessGrant>().AsNoTracking().Where(x => x.UserId == owner && x.ReversedAt == null).OrderBy(x => x.StartsAt).ToListAsync(ct);

    public async Task<Result> AddWalletAsync(TrackedWallet wallet, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(db, wallet.UserId, ct);
        var a = await AccountAsync(wallet.UserId, ct);
        if (await db.TrackedWallets.AnyAsync(x => x.UserId == wallet.UserId && x.Address == wallet.Address, ct)) return Result.Failure(Errors.TrackedWallet.AlreadyTracked);
        var pro = (await GrantsAsync(wallet.UserId, ct)).Any(x => x.Contains(clock.GetUtcNow()));
        if (options.Value.EnforcementEnabled && await db.TrackedWallets.CountAsync(x => x.UserId == wallet.UserId, ct) >= (pro ? BillingPlans.ProWallets : BillingPlans.FreeWallets)) return Result.Failure(BillingErrors.Limit);
        db.Add(wallet);
        var ids = JsonSerializer.Deserialize<List<Guid>>(a.FreeWalletIds)!;
        if (ids.Count < BillingPlans.FreeWallets) { ids.Add(wallet.Id); a.FreeWalletIds = JsonSerializer.Serialize(ids); }
        a.Revision++;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await RefreshAsync(wallet.UserId, ct);
        return Result.Success();
    }

    /// <summary>Called while owner and protocol admission locks are held by a settings mutation.</summary>
    public async Task<Result> CanEnableAsync(long userId, Guid walletId, TrackingMode mode, CancellationToken ct)
    {
        if (!options.Value.EnforcementEnabled) return Result.Success();
        var a = await AccountAsync(userId, ct);
        var pro = (await GrantsAsync(userId, ct)).Any(x => x.Contains(clock.GetUtcNow()));
        var ids = JsonSerializer.Deserialize<List<Guid>>(a.FreeWalletIds)!;
        if (!pro && !ids.Contains(walletId)) return Result.Failure(BillingErrors.Selection);
        if (pro && !await db.TrackedWallets.Where(x => x.UserId == userId).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .Take(BillingPlans.ProWallets).AnyAsync(x => x.Id == walletId, ct)) return Result.Failure(BillingErrors.Limit);
        if (mode == TrackingMode.Hyperliquid)
        {
            if (!pro) return Result.Failure(BillingErrors.ProRequired);
            if (await (from s in db.Set<HyperliquidSetting>() join w in db.TrackedWallets on s.WalletId equals w.Id
                where w.UserId == userId && w.Id != walletId && s.Enabled select s).CountAsync(ct) >= BillingPlans.ProHyperliquid) return Result.Failure(BillingErrors.Limit);
        }
        if (mode == TrackingMode.Polymarket && !pro)
        {
            if (a.FreePolymarketWalletId is { } existing && existing != walletId) return Result.Failure(BillingErrors.Limit);
            a.FreePolymarketWalletId = walletId;
        }
        return Result.Success();
    }

    public async Task<BillingSummary> SummaryAsync(long userId, CancellationToken ct)
    {
        await RefreshAsync(userId, ct);
        var a = await db.Set<BillingAccount>().AsNoTracking().SingleAsync(x => x.UserId == userId, ct);
        var grants = await GrantsAsync(userId, ct); var now = clock.GetUtcNow(); var pro = grants.Any(x => x.Contains(now));
        var wallets = await db.TrackedWallets.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        return new(userId, a.Revision, pro, wallets.Count, wallets.Count(x => policy.Allows(x.Id, TrackingMode.Evm)),
            grants.Where(x => x.EndsAt > now).Select(x => (DateTimeOffset?)x.EndsAt).Max(), JsonSerializer.Deserialize<List<Guid>>(a.FreeWalletIds)!, a.FreePolymarketWalletId, grants,
            Math.Max(0, hlOptions.Value.MaxUniqueWallets - await db.Set<HyperliquidReservation>().CountAsync(ct)),
            Math.Max(0, pmOptions.Value.MaxUniqueAddresses - await db.Set<PolymarketSetting>().Where(x => x.Enabled && !x.BillingPaused && x.SourceAddress != null).Select(x => x.SourceAddress).Distinct().CountAsync(ct)));
    }

    public async Task<Result> SelectAsync(long userId, IReadOnlyCollection<Guid> wallets, Guid? polymarketWallet, long revision, CancellationToken ct)
    {
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockOwnerAsync(db, userId, ct); var a = await AccountAsync(userId, ct);
            if (a.Revision != revision) return Result.Failure(BillingErrors.Stale);
            var ids = wallets.Distinct().ToList();
            if (ids.Count > BillingPlans.FreeWallets || await db.TrackedWallets.CountAsync(x => x.UserId == userId && ids.Contains(x.Id), ct) != ids.Count
                || polymarketWallet is { } pm && (!ids.Contains(pm) || !await db.Set<PolymarketSetting>().AnyAsync(x => x.WalletId == pm && x.SourceAddress != null && !x.NeedsAttention, ct))) return Result.Failure(BillingErrors.InvalidSelection);
            a.FreeWalletIds = JsonSerializer.Serialize(ids); a.FreePolymarketWalletId = polymarketWallet; a.Revision++;
            if (polymarketWallet is { } chosen)
            {
                var setting = await db.Set<PolymarketSetting>().SingleAsync(x => x.WalletId == chosen, ct);
                if (!setting.Enabled) { setting.Enabled = true; setting.BillingPaused = true; setting.Generation = Guid.NewGuid(); }
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        await RefreshAsync(userId, ct); return Result.Success();
    }

    public async Task<Result<PurchaseOrder>> CreateOrderAsync(long userId, bool renewal, CancellationToken ct)
    {
        if (userId <= 0 || !(options.Value.StarsSalesEnabled || options.Value.CryptoSalesEnabled)) return Result<PurchaseOrder>.Failure(BillingErrors.Unavailable);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockOwnerAsync(db, userId, ct);
        await AccountAsync(userId, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230004)", ct); var now = clock.GetUtcNow();
        var existing = await db.Set<PurchaseOrder>().Where(x => x.UserId == userId && !x.Fulfilled && x.CheckoutExpiresAt > now).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null) return Result<PurchaseOrder>.Success(existing);
        if (await db.Set<PurchaseOrder>().CountAsync(x => x.UserId == userId && x.CreatedAt > now.AddHours(-1), ct) >= 5
            || await db.Set<PurchaseOrder>().CountAsync(x => x.CreatedAt > now.AddMinutes(-1), ct) >= 100) return Result<PurchaseOrder>.Failure(BillingErrors.Unavailable);
        var order = new PurchaseOrder { Id = Guid.NewGuid(), UserId = userId, CreatedAt = now, CheckoutExpiresAt = now.AddHours(1) };
        db.Add(order); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result<PurchaseOrder>.Success(order);
    }

    public async Task<Result<PaymentAttempt>> PrepareAttemptAsync(long userId, Guid orderId, string provider, CancellationToken ct)
    {
        if (provider != BillingPlans.StarsProvider && provider != BillingPlans.CryptoProvider
            || provider == BillingPlans.StarsProvider && !options.Value.StarsSalesEnabled || provider == BillingPlans.CryptoProvider && !options.Value.CryptoSalesEnabled) return Result<PaymentAttempt>.Failure(BillingErrors.Unavailable);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockOwnerAsync(db, userId, ct);
        var order = await db.Set<PurchaseOrder>().SingleOrDefaultAsync(x => x.Id == orderId && x.UserId == userId, ct);
        if (order is null || order.Fulfilled || order.CheckoutExpiresAt <= clock.GetUtcNow()) return Result<PaymentAttempt>.Failure(BillingErrors.InvalidPayment);
        var attempt = await db.Set<PaymentAttempt>().SingleOrDefaultAsync(x => x.OrderId == orderId && x.Provider == provider, ct);
        if (attempt is null)
        {
            attempt = new() { Id = Guid.NewGuid(), OrderId = orderId, Provider = provider, CreatedAt = clock.GetUtcNow(), RetryAt = clock.GetUtcNow(),
                Currency = provider == BillingPlans.StarsProvider ? BillingPlans.Xtr : BillingPlans.UsdtBsc,
                ExpectedAmount = provider == BillingPlans.StarsProvider ? order.StarsPrice : null };
            db.Add(attempt);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Result<PaymentAttempt>.Success(attempt);
    }

    public async Task<Result> PreCheckoutAsync(long userId, Guid attemptId, string currency, long total, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await LockOwnerAsync(db, userId, ct);
        var valid = await (from a in db.Set<PaymentAttempt>() join o in db.Set<PurchaseOrder>() on a.OrderId equals o.Id
            where a.Id == attemptId && a.Provider == BillingPlans.StarsProvider && o.UserId == userId && !o.Fulfilled
                && o.CheckoutExpiresAt > clock.GetUtcNow() && currency == BillingPlans.Xtr && total == o.StarsPrice select a).AnyAsync(ct);
        return valid && options.Value.StarsSalesEnabled ? Result.Success() : Result.Failure(BillingErrors.InvalidPayment);
    }

    public async Task ReceiveAsync(FinancialReceipt input, CancellationToken ct)
    {
        // Serialize receipt identity and account ledger. Unknown receipts use the same receipt lock before owner lookup.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230001)", ct);
        var existingReceipt = await db.Set<PaymentReceipt>().SingleOrDefaultAsync(x => x.Provider == input.Provider && x.EventKey == input.EventKey, ct);
        if (existingReceipt is not null && !(existingReceipt.State == BillingStates.Review && input.Verified && input.AttemptId is not null)) return;
        var attempt = input.AttemptId is { } id ? await db.Set<PaymentAttempt>().SingleOrDefaultAsync(x => x.Id == id && x.Provider == input.Provider, ct) : null;
        var order = attempt is null ? null : await db.Set<PurchaseOrder>().SingleOrDefaultAsync(x => x.Id == attempt.OrderId, ct);
        if (order is not null) { await LockOwnerAsync(db, order.UserId, ct); await db.Entry(order).ReloadAsync(ct); }
        var receipt = existingReceipt ?? new PaymentReceipt { Id = Guid.NewGuid(), Provider = input.Provider, EventKey = input.EventKey, ChargeId = input.ChargeId,
            AttemptId = attempt?.Id, UserId = input.UserId, Currency = input.Currency, Amount = input.Amount,
            Reversal = input.Reversal, Evidence = input.Evidence.Length > 8192 ? input.Evidence[..8192] : input.Evidence, ReceivedAt = clock.GetUtcNow() };
        if (existingReceipt is null) db.Add(receipt);
        else { receipt.AttemptId = attempt?.Id; receipt.UserId = input.UserId; receipt.Currency = input.Currency; receipt.Amount = input.Amount; }
        var valid = input.Verified && order is not null && attempt is not null && (input.UserId is null || input.UserId == order.UserId)
            && input.Currency == attempt.Currency && attempt.ExpectedAmount is { } expected && input.Amount >= expected
            && !string.IsNullOrWhiteSpace(input.ChargeId)
            && (input.Provider != BillingPlans.StarsProvider || input.UserId == order.UserId && input.Amount == order.StarsPrice);
        if (input.Reversal)
        {
            var original = await db.Set<PaymentReceipt>().Where(x => x.Provider == input.Provider && x.ChargeId == input.ChargeId && !x.Reversal
                && (x.State == BillingStates.Applied || x.State == BillingStates.DuplicatePayment)).OrderByDescending(x => x.State == BillingStates.Applied).ThenBy(x => x.ReceivedAt).FirstOrDefaultAsync(ct);
            var grant = original is null ? null : await db.Set<AccessGrant>().SingleOrDefaultAsync(x => x.ReceiptId == original.Id, ct);
            if (valid && original is not null && original.AttemptId == attempt!.Id && original.Amount == input.Amount)
            {
                if (grant is not null) grant.ReversedAt = clock.GetUtcNow();
                // A refunded duplicate child has no grant and must not change its winning primary.
                if (grant is not null || input.Provider != BillingPlans.CryptoProvider || attempt!.ExternalId is null || attempt.ExternalId == input.ChargeId)
                    attempt!.State = BillingStates.Reversed;
                receipt.State = BillingStates.Reversed;
                Notice(order!.UserId, BillingKeys.Receipt(receipt.Id), BillingStates.Reversed);
            }
            else receipt.State = valid ? BillingStates.ReversalPending : BillingStates.Review;
        }
        else if (!valid) receipt.State = BillingStates.Review;
        else if (order!.Fulfilled || await db.Set<PaymentReceipt>().AnyAsync(x => x.Provider == input.Provider && x.ChargeId == input.ChargeId && !x.Reversal && x.State == BillingStates.Applied, ct)) receipt.State = BillingStates.DuplicatePayment;
        else
        {
            var now = clock.GetUtcNow(); var grants = await GrantsAsync(order.UserId, ct);
            var start = now; // Extend only a contiguous current paid period; future isolated grants retain their dates.
            foreach (var g in grants.Where(x => x.EndsAt > now)) if (g.StartsAt <= start && g.EndsAt > start) start = g.EndsAt;
            var reversed = await db.Set<PaymentReceipt>().AnyAsync(x => x.Provider == input.Provider && x.ChargeId == input.ChargeId && x.Reversal && (x.State == BillingStates.Reversed || x.State == BillingStates.ReversalPending), ct);
            db.Add(new AccessGrant { Id = Guid.NewGuid(), UserId = order.UserId, OrderId = order.Id, ReceiptId = receipt.Id, StartsAt = start, EndsAt = start.AddHours(order.DurationHours), ReversedAt = reversed ? now : null });
            order.Fulfilled = true; attempt!.State = reversed ? BillingStates.Reversed : BillingStates.Settled; attempt.PaidAmount = input.Amount; receipt.State = BillingStates.Applied;
            Notice(order.UserId, BillingKeys.Purchase(order.Id), reversed ? BillingStates.Reversed : BillingStates.Activated);
        }
        if (order is not null)
        {
            var account = await AccountAsync(order.UserId, ct); account.Revision++;
            if (receipt.State is BillingStates.Review or BillingStates.DuplicatePayment || receipt.Amount > attempt!.ExpectedAmount) Notice(order.UserId, BillingKeys.Receipt(receipt.Id), BillingStates.PaymentReview);
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (order is not null) await RefreshAsync(order.UserId, ct);
    }

    private void Notice(long owner, string key, string kind)
    {
        if (db.Set<BillingOutbox>().Local.Any(x => x.Key == key) || db.Set<BillingOutbox>().Any(x => x.Key == key)) return;
        db.Add(new BillingOutbox { Key = key, UserId = owner, Kind = kind, CreatedAt = clock.GetUtcNow(), RetryAt = clock.GetUtcNow() });
    }

    public async Task RefreshAsync(long userId, CancellationToken ct)
    {
        long? restorationHead = null;
        var verifiedSources = new Dictionary<Guid, (Guid Generation, string? Source)>();
        // Only eligible paused preferences need external restoration work. Permanently Free-paused entries
        // must not repeatedly call providers or delay projection freshness for the rest of the owner.
        var preliminaryAccount = await db.Set<BillingAccount>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        var preliminaryPro = (await GrantsAsync(userId, ct)).Any(x => x.Contains(clock.GetUtcNow()));
        var freeIds = preliminaryAccount is null ? [] : JsonSerializer.Deserialize<List<Guid>>(preliminaryAccount.FreeWalletIds)!;
        var priorAccess = await (from state in db.Set<WalletAccessState>().AsNoTracking() join w in db.TrackedWallets on state.WalletId equals w.Id
            where w.UserId == userId select state).ToDictionaryAsync(x => x.WalletId, ct);
        var restore = await (from s in db.Set<PolymarketSetting>().AsNoTracking() join w in db.TrackedWallets on s.WalletId equals w.Id
            where w.UserId == userId && s.Enabled && !s.NeedsAttention && s.SourceAddress != null
            select s).ToListAsync(ct);
        restore = restore.Where(s => (s.BillingPaused || options.Value.EnforcementEnabled && priorAccess.TryGetValue(s.WalletId, out var state) && state.PolymarketUntil <= clock.GetUtcNow())
            && (!options.Value.EnforcementEnabled || preliminaryPro || freeIds.Contains(s.WalletId) && preliminaryAccount?.FreePolymarketWalletId == s.WalletId)).ToList();
        if (rpc is not null && pmOptions.Value.Enabled && restore.Count > 0)
        {
            using var restorationBudget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            restorationBudget.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                foreach (var setting in restore)
                {
                    if (profiles is null) continue;
                    var identity = setting.ProfileJson is null ? setting.SourceAddress! : JsonSerializer.Deserialize<PolymarketProfile>(setting.ProfileJson)?.ResolvedAddress ?? setting.SourceAddress!;
                    var resolved = await profiles.ResolveAsync(identity, restorationBudget.Token);
                    if (!resolved.Stale && resolved.State == RuntimeStates.Profile.Found && resolved.Profile?.TradingAddress == setting.SourceAddress) verifiedSources[setting.WalletId] = (setting.Generation, setting.SourceAddress);
                }
                await rpc.VerifyChainAsync(restorationBudget.Token); restorationHead = await rpc.HeadAsync(restorationBudget.Token);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(db, userId, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74210001); SELECT pg_advisory_xact_lock(74220001)", ct);
        var a = await AccountAsync(userId, ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74230004)", ct); var now = clock.GetUtcNow(); var grants = await GrantsAsync(userId, ct);
        var active = grants.Where(x => x.Contains(now)).ToList(); var pro = active.Count > 0;
        var validUntil = now.AddSeconds(options.Value.ProjectionFreshnessSeconds);
        var paidUntil = pro ? active.Max(x => x.EndsAt) : (DateTimeOffset?)null;
        if (paidUntil is not null) foreach (var g in grants) if (g.StartsAt <= paidUntil && g.EndsAt > paidUntil) paidUntil = g.EndsAt;
        var wallets = await db.TrackedWallets.AsNoTracking().Where(x => x.UserId == userId).OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(ct);
        var ids = JsonSerializer.Deserialize<List<Guid>>(a.FreeWalletIds)!;
        var proIds = wallets.Take(BillingPlans.ProWallets).Select(x => x.Id).ToHashSet();
        var hls = await db.Set<HyperliquidSetting>().Where(x => wallets.Select(w => w.Id).Contains(x.WalletId)).ToListAsync(ct);
        var pms = await db.Set<PolymarketSetting>().Where(x => wallets.Select(w => w.Id).Contains(x.WalletId)).ToListAsync(ct);
        foreach (var setting in hls) await db.Entry(setting).ReloadAsync(ct);
        foreach (var setting in pms) await db.Entry(setting).ReloadAsync(ct);
        var pmGenerations = pms.ToDictionary(x => x.WalletId, x => x.Generation);
        var hlCount = 0; var projections = new List<WalletEntitlement>(); var changed = false;
        var durableAccess = await db.Set<WalletAccessState>().Where(x => wallets.Select(w => w.Id).Contains(x.WalletId)).ToDictionaryAsync(x => x.WalletId, ct);
        // Known paid bounds survive restarts and cross-host renewals. An expired boundary must close
        // the old enrollment even if the expiry worker did not run before a later payment arrived.
        if (options.Value.EnforcementEnabled)
        {
            foreach (var setting in hls.Where(x => x.Enabled && !x.BillingPaused && durableAccess.TryGetValue(x.WalletId, out var state) && state.HyperliquidUntil <= now))
            { setting.BillingPaused = true; setting.Generation = Guid.NewGuid(); changed = true; }
            foreach (var setting in pms.Where(x => x.Enabled && !x.BillingPaused && durableAccess.TryGetValue(x.WalletId, out var state) && state.PolymarketUntil <= now))
            { setting.BillingPaused = true; setting.Generation = Guid.NewGuid(); changed = true; }
        }
        // Pause outgoing recipients before testing incoming source capacity. Both sides commit together.
        foreach (var setting in pms.Where(s => !s.BillingPaused))
        {
            var eligible = !options.Value.EnforcementEnabled || pro && proIds.Contains(setting.WalletId)
                || !pro && ids.Contains(setting.WalletId) && a.FreePolymarketWalletId == setting.WalletId;
            if (eligible) continue;
            setting.BillingPaused = true; setting.Generation = Guid.NewGuid(); setting.ActivatedAt = now; changed = true;
            setting.BillingPauseReason = BillingDiagnostics.FreeSelectionPaused;
            await db.Set<PolymarketDelivery>().Where(x => x.WalletId == setting.WalletId && x.State == RuntimeStates.Delivery.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, RuntimeStates.Delivery.Cancelled), ct);
        }
        await db.SaveChangesAsync(ct);

        foreach (var w in wallets)
        {
            var enabled = !options.Value.EnforcementEnabled || pro && proIds.Contains(w.Id) || !pro && ids.Contains(w.Id);
            var hl = hls.SingleOrDefault(x => x.WalletId == w.Id); var pm = pms.SingleOrDefault(x => x.WalletId == w.Id);
            var hlAccess = enabled && (!options.Value.EnforcementEnabled || pro && (hl?.Enabled != true || hlCount++ < BillingPlans.ProHyperliquid));
            var pmAccess = enabled && (!options.Value.EnforcementEnabled || pro || a.FreePolymarketWalletId == w.Id);
            if (hl is not null)
            {
                var paused = !hlAccess;
                hl.BillingPauseReason = paused ? pro ? BillingDiagnostics.FreeSelectionPaused : BillingDiagnostics.ProRequired : null;
                if (!paused && hl.Enabled)
                {
                    var address = w.Address.Value.ToLowerInvariant();
                    if (!await db.Set<HyperliquidReservation>().AnyAsync(x => x.Address == address, ct))
                    {
                        if (await db.Set<HyperliquidReservation>().CountAsync(ct) + db.ChangeTracker.Entries<HyperliquidReservation>().Count(x => x.State == EntityState.Added) >= Math.Clamp(hlOptions.Value.MaxUniqueWallets, 1, 10))
                        { paused = true; hl.BillingPauseReason = BillingDiagnostics.CapacityPaused; }
                        else db.Add(new HyperliquidReservation { Address = address });
                    }
                }
                if (hl.BillingPaused != paused)
                {
                    hl.BillingPaused = paused; hl.Generation = Guid.NewGuid(); hl.ActivatedAt = now; changed = true;
                    await db.Set<HyperliquidDelivery>().Where(x => x.WalletId == w.Id && x.State == RuntimeStates.Delivery.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, RuntimeStates.Delivery.Cancelled), ct);
                }
                hlAccess &= !paused;
            }
            if (pm is not null)
            {
                var paused = !pmAccess;
                pm.BillingPauseReason = paused ? BillingDiagnostics.FreeSelectionPaused : null;
                if (pm.BillingPaused && !paused && pm.Enabled)
                {
                    var count = await db.Set<PolymarketSetting>().Where(x => x.Enabled && !x.BillingPaused && x.SourceAddress != null).Select(x => x.SourceAddress).Distinct().CountAsync(ct);
                    if (restorationHead is null || !verifiedSources.TryGetValue(pm.WalletId, out var verified) || verified.Source != pm.SourceAddress
                        || verified.Generation != pmGenerations[pm.WalletId] || pm.NeedsAttention || pm.SourceAddress is null)
                    { paused = true; pm.BillingPauseReason = BillingDiagnostics.VerificationPaused; }
                    else if (count >= pmOptions.Value.MaxUniqueAddresses && !await db.Set<PolymarketSetting>().AnyAsync(x => x.Enabled && !x.BillingPaused && x.SourceAddress == pm.SourceAddress, ct))
                    { paused = true; pm.BillingPauseReason = BillingDiagnostics.CapacityPaused; }
                    else pm.ActivationBlock = restorationHead;
                }
                if (pm.BillingPaused != paused)
                {
                    pm.BillingPaused = paused; pm.Generation = Guid.NewGuid(); pm.ActivatedAt = now; changed = true;
                    await db.Set<PolymarketDelivery>().Where(x => x.WalletId == w.Id && x.State == RuntimeStates.Delivery.Pending).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, RuntimeStates.Delivery.Cancelled), ct);
                }
                pmAccess &= !paused;
                // Subsequent restorations in this transaction must count earlier restored sources.
                // The admission lock remains held until every change commits atomically.
                await db.SaveChangesAsync(ct);
            }
            if (!durableAccess.TryGetValue(w.Id, out var state))
            { state = new WalletAccessState { WalletId = w.Id }; db.Add(state); durableAccess[w.Id] = state; }
            var previousProjection = policy.Get(w.Id);
            var continuous = state.EvmEnabled && state.EvmUntil > now && enabled
                && (previousProjection is null || previousProjection.ValidUntil > now);
            if (state.EvmEnabled != enabled || enabled && !continuous) { state.EnrollmentRevision++; state.ActiveSince = now; }
            state.EvmEnabled = enabled;
            state.EvmUntil = options.Value.EnforcementEnabled && pro && !ids.Contains(w.Id) ? paidUntil!.Value : DateTimeOffset.MaxValue;
            var evmUntil = state.EvmUntil < validUntil ? state.EvmUntil : validUntil;
            var pmUntil = ids.Contains(w.Id) && a.FreePolymarketWalletId == w.Id ? (DateTimeOffset?)null : paidUntil;
            state.PolymarketUntil = pmUntil; state.HyperliquidUntil = paidUntil;
            projections.Add(new(userId, w.Id, state.EnrollmentRevision, enabled, pmAccess, hlAccess, evmUntil, state.ActiveSince, pmUntil, paidUntil));
        }
        if (a.WasPro != pro)
        {
            a.Revision++; changed = true;
            if (a.WasPro) Notice(userId, BillingKeys.Expiry(userId, a.Revision), BillingStates.Expired);
            a.WasPro = pro;
        }
        foreach (var state in durableAccess.Values) state.AccountRevision = a.Revision;

        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        policy.Replace(userId, a.Revision, now, projections);
        foreach (var w in wallets) { if (policy.Allows(w.Id, TrackingMode.Evm)) cache.Add(w); else cache.Remove(w); }
        if (changed) signal.Changed();
    }
}
