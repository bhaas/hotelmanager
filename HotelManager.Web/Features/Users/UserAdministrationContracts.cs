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
}
