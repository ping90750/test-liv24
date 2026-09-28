namespace OrderProcessing.Domain;

public class Coupon
{
    public required string Code { get; set; }

    /// <summary>Percentage off, e.g. 10 means 10%.</summary>
    public decimal PercentOff { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;
}
