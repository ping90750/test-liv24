using OrderProcessing.Common;

namespace OrderProcessing.Application;

public static class OrderErrors
{
    public static readonly Error MissingIdempotencyKey =
        new("order.missing_idempotency_key", "The Idempotency-Key header is required.", ErrorKind.Validation);

    public static readonly Error InvalidUser =
        new("order.invalid_user", "The user does not exist or is inactive.", ErrorKind.Validation);

    public static readonly Error EmptyOrder =
        new("order.empty", "The order has no items.", ErrorKind.Validation);

    public static readonly Error InvalidQuantity =
        new("order.invalid_quantity", "Every item quantity must be greater than zero.", ErrorKind.Validation);

    public static readonly Error InvalidCoupon =
        new("order.invalid_coupon", "The coupon is unknown, inactive or expired.", ErrorKind.Validation);

    public static readonly Error ProductNotFound =
        new("order.product_not_found", "One or more products do not exist.", ErrorKind.NotFound);

    public static readonly Error InsufficientStock =
        new("order.insufficient_stock", "One or more products do not have enough stock.", ErrorKind.Conflict);
}
