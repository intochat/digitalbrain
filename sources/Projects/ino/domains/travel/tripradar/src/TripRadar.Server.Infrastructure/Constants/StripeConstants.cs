namespace TripRadar.Server.Infrastructure.Constants;

public static class StripeConstants
{
    public static class CouponDuration
    {
        public const string Once = "once";
    }

    public static class SessionMode
    {
        public const string Subscription = "subscription";
    }

    public static class Metadata
    {
        public const string PromoCode = "promo_code";
        public const string PromoCodeId = "promo_code_id";
        public const string DiscountType = "discount_type";
        public const string DiscountValue = "discount_value";
        public const string UserId = "user_id";
        public const string Year = "year";
        public const string Month = "month";
        public const string Source = "source";
        public const string ProcessingId = "processing_id";
    }

    public static class DiscountType
    {
        public const string Percentage = "percentage";
        public const string Fixed = "fixed";
    }

    public static class WebhookEvents
    {
        public static class Subscription
        {
            public const string Created = "customer.subscription.created";
            public const string Updated = "customer.subscription.updated";
            public const string Deleted = "customer.subscription.deleted";
            public const string Canceled = "customer.subscription.canceled";
        }

        public static class Invoice
        {
            public const string PaymentSucceeded = "invoice.payment_succeeded";
        }

        public static class InvoiceItem
        {
            public const string Created = "invoiceitem.created";
        }

        public static class PaymentIntent
        {
            public const string Succeeded = "payment_intent.succeeded";
            public const string Created = "payment_intent.created";
        }

        public static class Charge
        {
            public const string Succeeded = "charge.succeeded";
        }

        public static class CheckoutSession
        {
            public const string Completed = "checkout.session.completed";
        }
    }
}
