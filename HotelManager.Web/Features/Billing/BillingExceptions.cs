namespace HotelManager.Web.Features.Billing;

public sealed class BillingValidationException(string message) : Exception(message);

public sealed class BillingNotFoundException(string message) : Exception(message);

public sealed class BillingConflictException(string message) : Exception(message);
