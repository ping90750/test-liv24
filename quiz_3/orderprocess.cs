public class OrderService
{
private readonly AppDbContext _db;

private readonly PaymentGateway _paymentGateway;
private readonly EmailService _emailService;
private readonly ILogger<OrderService> _logger;

public OrderService(
AppDbContext db,
PaymentGateway paymentGateway,
EmailService emailService,
ILogger<OrderService> logger)
{
_db = db;
_paymentGateway = paymentGateway;
_emailService = emailService;
_logger = logger;
}

public async Task<string> ProcessOrderAsync(CreateOrderRequest request)
{
var user = await _db.Users.FindAsync(request.UserId);
if (user == null || !user.IsActive)
return "Invalid user";

if (request.Items == null || !request.Items.Any())
return "Empty order";

decimal total = 0;

foreach (var item in request.Items)
{
var product = await _db.Products.FindAsync(item.ProductId);

if (product == null)
return "Product not found";

if (product.Stock < item.Quantity)
return "Insufficient stock";

total += product.Price * item.Quantity;
}

// Apply coupon
if (!string.IsNullOrEmpty(request.CouponCode))
{
if (request.CouponCode == "DIS10")
total *= 0.9m;
else if (request.CouponCode == "DIS20")
total *= 0.8m;
}

if (total > 50000)
total -= 2000;

var order = new Order
{
UserId = user.Id,
CreatedAt = DateTime.UtcNow,
TotalAmount = total,
Status = "Pending"
};

_db.Orders.Add(order);
await _db.SaveChangesAsync();

// Deduct stock
foreach (var item in request.Items)
{
var product = await _db.Products.FindAsync(item.ProductId);
product.Stock -= item.Quantity;
}

await _db.SaveChangesAsync();

// Charge payment
var paymentResult = await _paymentGateway.ChargeAsync(user.CardNumber, total);

if (!paymentResult.Success)
{
order.Status = "PaymentFailed";
await _db.SaveChangesAsync();
return "Payment failed";
}

order.Status = "Completed";
await _db.SaveChangesAsync();

await _emailService.SendAsync(user.Email, "Order completed");

return "Success";
}
}