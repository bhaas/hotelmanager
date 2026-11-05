namespace HotelManager.Web.Features.Users;

/// <summary>A member of the current hotel.</summary>
/// <param name="IsLocked">Access to this hotel is deactivated (or the account is locked).</param>
/// <param name="CanManageAccount">
/// Name and password may be changed here. False for accounts that also belong to other hotels
/// or are platform administrators: those are managed by the platform administrator.
/// </param>
public sealed record UserSummary(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsLocked,
    DateTimeOffset CreatedAtUtc,
    bool IsCurrentUser,
    int OtherHotelCount,
    bool CanManageAccount);

public sealed record UserCreateRequest(string Email, string DisplayName, string Role);

public sealed record UserUpdateRequest(string DisplayName, string Role);

/// <summary>
/// A user together with a one-time password that must be handed over to the user.
/// <see cref="Password"/> is null when an existing account was added to the hotel (it keeps its password).
/// </summary>
public sealed record UserPasswordResult(UserSummary User, string? Password);

/// <summary>A user's access to a hotel other than the current one.</summary>
/// <param name="Role">The membership role, or null when the user has never had access to this hotel.</param>
/// <param name="HasAccess">True while the membership is active.</param>
/// <param name="CanEdit">The caller administers this hotel and may grant, change or revoke the access.</param>
public sealed record UserHotelAccess(Guid HotelId, string HotelName, string? City, string? Role, bool HasAccess, bool CanEdit);

/// <summary>Grants access to a hotel with <see cref="Role"/>, or revokes it when <see cref="Role"/> is null.</summary>
public sealed record UserHotelAssignment(Guid HotelId, string? Role);

public sealed class UserAdministrationException(string message) : Exception(message);

public interface IUserAdministrationService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an account, or adds an existing account (same email) to the current hotel.</summary>
    Task<UserPasswordResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default);

    Task<UserSummary> UpdateAsync(string userId, UserUpdateRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deactivates or reactivates the user's access to the current hotel.</summary>
    Task<UserSummary> SetLockedAsync(string userId, bool locked, CancellationToken cancellationToken = default);

    Task<UserPasswordResult> ResetPasswordAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The other hotels the caller administers (editable) plus the other hotels the user already belongs to (read-only
    /// unless administered). Pass null for a user that is about to be created.
    /// </summary>
    Task<IReadOnlyList<UserHotelAccess>> GetOtherHotelsAsync(string? userId, CancellationToken cancellationToken = default);

    /// <summary>Grants, changes or revokes the user's access to other hotels the caller administers.</summary>
    Task<UserSummary> SetOtherHotelsAsync(string userId, IReadOnlyList<UserHotelAssignment> assignments, CancellationToken cancellationToken = default);
}
