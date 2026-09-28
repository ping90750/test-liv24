namespace OrderProcessing.Domain;

public sealed record OrderLine(int ProductId, int Quantity);

/// <summary>An order line with the price read from the database at order time.</summary>
public sealed record PricedLine(int ProductId, string ProductName, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

public sealed record PriceBreakdown(decimal Subtotal, decimal Discount, decimal Total);
