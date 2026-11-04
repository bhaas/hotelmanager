namespace HotelManager.Web.Features.Users;

public sealed record UserSummary(
    string Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsLocked,
    DateTimeOffset CreatedAtUtc,
    bool IsCurrentUser);

public sealed record UserCreateRequest(string Email, string DisplayName, string Role);

public sealed record UserUpdateRequest(string DisplayName, string Role);

/// <summary>A user together with a one-time password that must be handed over to the user.</summary>
public sealed record UserPasswordResult(UserSummary User, string Password);

public sealed class UserAdministrationException(string message) : Exception(message);

public interface IUserAdministrationService
{
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<UserPasswordResult> CreateAsync(UserCreateRequest request, CancellationToken cancellationToken = default);

    Task<UserSummary> UpdateAsync(string userId, UserUpdateRequest request, CancellationToken cancellationToken = default);

    Task<UserSummary> SetLockedAsync(string userId, bool locked, CancellationToken cancellationToken = default);

    Task<UserPasswordResult> ResetPasswordAsync(string userId, CancellationToken cancellationToken = default);
}
