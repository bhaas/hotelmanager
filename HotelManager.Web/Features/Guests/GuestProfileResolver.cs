using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Web.Features.Guests;

/// <summary>Guest data delivered by a channel (file import or channel manager).</summary>
public sealed record ChannelGuestData(
    string FullName,
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    string? Phone = null,
    string? Street = null,
    string? PostalCode = null,
    string? City = null,
    string? Country = null);

public static class GuestProfileResolver
{
    /// <summary>
    /// Returns the profile with the same e-mail address or adds a new one to <paramref name="dbContext"/> (saved by the caller).
    /// </summary>
    public static async Task<Guid> FindOrCreateAsync(HotelDbContext dbContext, ChannelGuestData data, CancellationToken cancellationToken)
    {
        var email = data.Email is { Length: <= 256 } candidate && candidate.Contains('@') ? candidate.Trim() : null;
        if (email is not null)
        {
            var lowerEmail = email.ToLowerInvariant();
            var match = await dbContext.Guests
                .Where(guest => guest.AnonymizedAtUtc == null && guest.Email != null && guest.Email.ToLower() == lowerEmail)
                .Select(guest => (Guid?)guest.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (match is { } id)
            {
                return id;
            }
        }

        var (firstName, lastName) = string.IsNullOrWhiteSpace(data.LastName)
            ? GuestNames.Split(data.FullName)
            : (string.IsNullOrWhiteSpace(data.FirstName) ? null : data.FirstName.Trim(), data.LastName.Trim());
        var guest = new Guest
        {
            Id = Guid.NewGuid(),
            FirstName = Truncate(firstName, 80),
            LastName = Truncate(lastName, 120)!,
            Email = email,
            Phone = Truncate(Blank(data.Phone), 40),
            Street = Truncate(Blank(data.Street), 200),
            PostalCode = Truncate(Blank(data.PostalCode), 16),
            City = Truncate(Blank(data.City), 100),
            Country = Truncate(Blank(data.Country), 100)
        };
        dbContext.Guests.Add(guest);
        return guest.Id;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Truncate(string? value, int maxLength) =>
        value is null ? null : value.Length <= maxLength ? value : value[..maxLength];
}
