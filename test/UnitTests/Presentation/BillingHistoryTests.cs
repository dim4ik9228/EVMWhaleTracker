using Application.Billing;
using Domain.Entities;
using Presentation.Telegram.Billing;

namespace UnitTests.Presentation;

public sealed class BillingHistoryTests
{
    [Fact]
    public void RecoveredHostedPaymentShowsOnePurchaseAndKeepsTransportEvidenceOutOfHistory()
    {
        var purchase = new PaymentReceipt { EventKey = BillingKeys.Settlement("paid", 0.99630628m),
            State = BillingStates.Applied, Amount = 0.99630628m, Currency = BillingPlans.UsdtBsc };
        PaymentReceipt[] ledger = [purchase,
            new() { EventKey = BillingKeys.Ipn("signed-finished"), State = BillingStates.Processed },
            new() { EventKey = BillingKeys.Discovered("paid"), State = BillingStates.Processed },
            new() { EventKey = BillingKeys.Quote("paid"), State = BillingStates.Processed,
                Amount = 0.99630628m, Currency = BillingPlans.UsdtBsc },
            new() { EventKey = BillingKeys.Settlement("paid", 0.99630628m) + ":review", State = BillingStates.Processed,
                Amount = 0.99630628m, Currency = BillingPlans.UsdtBsc },
            new() { EventKey = BillingKeys.Ipn("unresolved"), State = BillingStates.Review }];

        Assert.Same(purchase, Assert.Single(TelegramBilling.FinancialReceipts(ledger.AsQueryable())));
        Assert.Equal(6, ledger.Length); // Presentation filtering preserves operator evidence.
    }

    [Theory]
    [InlineData(BillingStates.Applied)]
    [InlineData(BillingStates.DuplicatePayment)]
    [InlineData(BillingStates.Review)]
    [InlineData(BillingStates.Reversed)]
    [InlineData(BillingStates.ReversalPending)]
    public void FinancialDepositsAndRefundsRemainVisible(string state)
    {
        var receipt = new PaymentReceipt { State = state, Amount = 1m, Currency = BillingPlans.UsdtBsc };
        Assert.Same(receipt, Assert.Single(TelegramBilling.FinancialReceipts(new[] { receipt }.AsQueryable())));
    }
}
