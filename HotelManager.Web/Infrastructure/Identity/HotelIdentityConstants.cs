namespace HotelManager.Web.Infrastructure.Identity;

public static class HotelClaimTypes
{
    public const string HotelId = "hotel_id";
    public const string HotelName = "hotel_name";
    public const string DisplayName = "display_name";
}

/// <summary>Per-hotel roles, stored in <c>HotelMembership.Role</c> and issued as role claims for the active hotel.</summary>
public static class HotelRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string FrontDesk = "FrontDesk";
    public const string Housekeeping = "Housekeeping";

    public static readonly IReadOnlyList<string> All = [Admin, Manager, FrontDesk, Housekeeping];
}

/// <summary>Installation-wide roles, stored as ASP.NET Core Identity roles.</summary>
public static class PlatformRoles
{
    /// <summary>Operator of the installation: sees and manages all hotels and acts as Admin in each of them.</summary>
    public const string PlatformAdmin = "PlatformAdmin";
}

public static class HotelPolicies
{
    /// <summary>Room planner, reservations and billing.</summary>
    public const string FrontOffice = "FrontOffice";

    /// <summary>Rooms, categories, rates and availability.</summary>
    public const string ManageProperty = "ManageProperty";

    /// <summary>User accounts, roles and the hotel profile.</summary>
    public const string ManageUsers = "ManageUsers";

    /// <summary>All hotels of the installation.</summary>
    public const string ManagePlatform = "ManagePlatform";
}
