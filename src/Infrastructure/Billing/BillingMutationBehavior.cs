using Application.TrackedWallets;
using Domain.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Billing;

/// <summary>Serializes existing wallet edits/removals with entitlement selection and refreshes the committed owner projection.</summary>
public sealed class BillingMutationBehavior<TRequest, TResponse>(AppDbContext db, BillingService billing)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var type = typeof(TRequest);
        if (type.Namespace != typeof(AddTrackedWalletCommand).Namespace || !type.Name.EndsWith("Command", StringComparison.Ordinal)
            || request is AddTrackedWalletCommand) return await next(cancellationToken);
        var owner = (long?)type.GetProperty("TelegramUserId")?.GetValue(request);
        if (owner is null) return await next(cancellationToken);
        TResponse response;
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await BillingService.LockOwnerAsync(db, owner.Value, cancellationToken);
            response = await next(cancellationToken);
            if (response is Result { IsFailure: true })
            {
                await tx.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                return response;
            }
            var account = await db.Set<BillingAccount>().SingleOrDefaultAsync(x => x.UserId == owner.Value, cancellationToken);
            if (account is not null) account.Revision++;
            await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken);
        }
        await billing.RefreshAsync(owner.Value, cancellationToken);
        return response;
    }
}
