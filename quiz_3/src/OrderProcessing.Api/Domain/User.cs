namespace OrderProcessing.Domain;

public class User
{
    public int Id { get; set; }

    public required string Email { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Token issued by the payment provider. Raw card numbers are never stored (PCI-DSS).
    /// </summary>
    public required string PaymentMethodToken { get; set; }
}
