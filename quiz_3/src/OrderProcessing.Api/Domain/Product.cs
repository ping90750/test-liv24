namespace OrderProcessing.Domain;

public class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    /// <summary>
    /// Only changed through atomic conditional updates in <c>IInventoryService</c>;
    /// the database also enforces <c>Stock &gt;= 0</c>.
    /// </summary>
    public int Stock { get; set; }
}
