namespace OrderProcessing.Domain;

/// <summary>
/// PendingPayment ─┬─▶ Paid
///                 ├─▶ PaymentFailed           (stock released)
///                 └─▶ PaymentUnknown ─┬─▶ Paid          (via reconciliation)
///                                     └─▶ PaymentFailed (via reconciliation)
/// </summary>
public enum OrderStatus
{
    PendingPayment,
    PaymentUnknown,
    Paid,
    PaymentFailed,
}
