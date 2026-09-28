namespace OrderProcessing.Domain;

public class OrderItem
{
    private OrderItem()
    {
    }

    internal OrderItem(Guid orderId, PricedLine line)
    {
        OrderId = orderId;
        ProductId = line.ProductId;
        ProductName = line.ProductName;
        UnitPrice = line.UnitPrice;
        Quantity = line.Quantity;
    }

    public Guid OrderId { get; private set; }

    public int ProductId { get; private set; }

    public string ProductName { get; private set; } = null!;

    /// <summary>Price at order time, so later price changes don't rewrite history.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }
}
