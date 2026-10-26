namespace HotelManager.Web.Features.RoomPlanner;

public sealed class ReservationValidationException(string message) : Exception(message);

public sealed class ReservationConflictException(string message) : Exception(message);

public sealed class ReservationNotFoundException(string message) : Exception(message);
