namespace HotelManager.Web.Infrastructure.Identity;

public static class HotelClaimTypes
{
    public const string HotelId = "hotel_id";
    public const string HotelName = "hotel_name";
    public const string DisplayName = "display_name";
}

public static class HotelRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string FrontDesk = "FrontDesk";
    public const string Housekeeping = "Housekeeping";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, FrontDesk, Housekeeping];
}

public static class HotelPolicies
{
    /// <summary>Room planner, reservations and billing.</summary>
    public const string FrontOffice = "FrontOffice";

    /// <summary>Rooms, categories, rates and availability.</summary>
    public const string ManageProperty = "ManageProperty";

    /// <summary>User accounts and roles.</summary>
    public const string ManageUsers = "ManageUsers";
}
