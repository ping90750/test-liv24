using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Application;
using OrderProcessing.Common;
using OrderProcessing.Domain;
using OrderProcessing.Infrastructure.Persistence;

namespace OrderProcessing.Api;

public sealed record CreateOrderRequest(int UserId, List<CreateOrderItem>? Items, string? CouponCode);

public sealed record CreateOrderItem(int ProductId, int Quantity);

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders");
        orders.MapPost("/", PlaceOrderAsync);
        orders.MapGet("/{id:guid}", GetOrderAsync);
        return app;
    }

    private static async Task<IResult> PlaceOrderAsync(
        CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        PlaceOrderHandler handler,
        CancellationToken ct)
    {
        // In production UserId comes from the authenticated principal, not the body.
        var command = new PlaceOrderCommand(
            request.UserId,
            request.Items?.Select(i => new OrderLine(i.ProductId, i.Quantity)).ToList(),
            request.CouponCode,
            idempotencyKey ?? string.Empty);

        var result = await handler.HandleAsync(command, ct);
        if (result.IsFailure)
            return ToProblem(result.Error);

        var order = result.Value;
        return order.Status switch
        {
            OrderStatus.Paid => Results.Created($"/orders/{order.OrderId}", order),
            OrderStatus.PaymentFailed => Results.Json(order, statusCode: StatusCodes.Status402PaymentRequired),
            // PendingPayment / PaymentUnknown: the client polls GET /orders/{id}.
            _ => Results.Accepted($"/orders/{order.OrderId}", order),
        };
    }

    private static async Task<IResult> GetOrderAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        return order is null ? Results.NotFound() : Results.Ok(OrderResult.From(order));
    }

    private static IResult ToProblem(Error error) => Results.Problem(
        title: error.Message,
        statusCode: error.Kind switch
        {
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        },
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
