using System.Security.Claims;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Integrations.Beds24;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelManager.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class Beds24Tests(DatabaseFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Beds24_sync_imports_bookings_adopts_file_imports_and_applies_changes_and_cancellations()
    {
        var hotelId = await fixture.CreateHotelAsync("Beds24 hotel");
        Guid fileImportId;
        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            db.RoomCategories.AddRange(
                new RoomCategory { Id = Guid.NewGuid(), Code = "DBL", Name = "Double", MaxOccupancy = 3 },
                new RoomCategory { Id = Guid.NewGuid(), Code = "SGL", Name = "Single", StandardOccupancy = 1, MaxOccupancy = 1 });
            var room101 = new Room { Id = Guid.NewGuid(), Number = "101", Type = "Double" };
            db.Rooms.AddRange(room101, new Room { Id = Guid.NewGuid(), Number = "102", Type = "Double" }, new Room { Id = Guid.NewGuid(), Number = "201", Type = "Single" });
            var fileImport = new Reservation
            {
                Id = Guid.NewGuid(),
                RoomId = room101.Id,
                GuestName = "Berta Booking",
                CheckIn = Today.AddDays(10),
                CheckOut = Today.AddDays(12),
                Status = ReservationStatus.Confirmed,
                Source = ReservationSource.BookingCom,
                ExternalReference = "5550001"
            };
            db.Reservations.Add(fileImport);
            fileImportId = fileImport.Id;
            await db.SaveChangesAsync();
        }

        var beds24 = new FakeBeds24Client();
        beds24.Bookings.AddRange(
        [
            Booking(1001, 11, Today.AddDays(3), Today.AddDays(5), "confirmed", adults: 2, children: 1, unitId: 502, firstName: "Anna", lastName: "Berger", email: "anna@example.com"),
            Booking(1002, 11, Today.AddDays(10), Today.AddDays(12), "confirmed", adults: 2, channel: "booking", apiReference: "5550001", lastName: "Booking"),
            Booking(1003, 13, Today.AddDays(3), Today.AddDays(4), "confirmed", lastName: "Suite Guest"),
            Booking(1004, 11, Today.AddDays(20), Today.AddDays(21), "cancelled", lastName: "Never Here")
        ]);

        await using (var scope = CreateAdminScope(hotelId))
        {
            var service = ActivatorUtilities.CreateInstance<Beds24SyncService>(scope.ServiceProvider, beds24);
            var status = await service.ConnectAsync("invite-code");
            Assert.Equal(77, status.PropertyId);

            var mappings = (await service.GetRoomMappingsAsync()).ToDictionary(mapping => mapping.Beds24RoomId, mapping => mapping.CategoryName);
            Assert.Equal("Double", mappings[11]);
            Assert.Equal("Single", mappings[12]);
            Assert.Null(mappings[13]);

            var result = await service.SyncNowAsync();
            Assert.Equal((1, 1, 0), (result.Created, result.Updated, result.Cancelled));
            Assert.Contains("Suite", Assert.Single(result.Problems));
        }

        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var anna = await db.Reservations.Include(item => item.Room).Include(item => item.Guest)
                .SingleAsync(item => item.Source == ReservationSource.Beds24 && item.ExternalReference == "1001");
            Assert.Equal("102", anna.Room.Number);
            Assert.Equal(3, anna.Occupancy);
            Assert.Equal("Anna Berger", anna.GuestName);
            Assert.Equal("anna@example.com", anna.Guest!.Email);

            var adopted = await db.Reservations.SingleAsync(item => item.Id == fileImportId);
            Assert.Equal((ReservationSource.Beds24, "1002", 2), (adopted.Source, adopted.ExternalReference, adopted.Occupancy));
            Assert.Equal(2, await db.Reservations.CountAsync());
        }

        beds24.Bookings.Clear();
        beds24.Bookings.AddRange(
        [
            Booking(1001, 11, Today.AddDays(4), Today.AddDays(6), "confirmed", adults: 2, children: 1, unitId: 502, firstName: "Anna", lastName: "Berger", email: "anna@example.com"),
            Booking(1002, 11, Today.AddDays(10), Today.AddDays(12), "cancelled", adults: 2, channel: "booking", apiReference: "5550001", lastName: "Booking")
        ]);

        await using (var scope = CreateAdminScope(hotelId))
        {
            var service = ActivatorUtilities.CreateInstance<Beds24SyncService>(scope.ServiceProvider, beds24);
            await service.SaveRoomMappingsAsync(new Dictionary<int, string?> { [13] = "Double" });
            var result = await service.SyncNowAsync();
            Assert.Equal((0, 1, 1), (result.Created, result.Updated, result.Cancelled));
            Assert.Empty(result.Problems);
            Assert.NotNull((await service.GetStatusAsync())!.LastSyncAtUtc);
        }

        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var anna = await db.Reservations.SingleAsync(item => item.ExternalReference == "1001");
            Assert.Equal((Today.AddDays(4), Today.AddDays(6)), (anna.CheckIn, anna.CheckOut));
            Assert.Equal(ReservationStatus.Cancelled, (await db.Reservations.SingleAsync(item => item.Id == fileImportId)).Status);
        }
    }

    private AsyncServiceScope CreateAdminScope(Guid hotelId)
    {
        var scope = fixture.CreateScope(hotelId);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "beds24-admin"), new Claim(HotelClaimTypes.HotelId, hotelId.ToString()), new Claim(ClaimTypes.Role, HotelRoles.Admin) };
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        return scope;
    }

    private static Beds24Booking Booking(
        int id,
        int roomId,
        DateOnly arrival,
        DateOnly departure,
        string status,
        int adults = 1,
        int children = 0,
        int? unitId = null,
        string? firstName = null,
        string? lastName = null,
        string? email = null,
        string? channel = null,
        string? apiReference = null) => new()
    {
        Id = id,
        PropertyId = 77,
        RoomId = roomId,
        UnitId = unitId,
        RoomQty = 1,
        Status = status,
        Arrival = arrival.ToString("yyyy-MM-dd"),
        Departure = departure.ToString("yyyy-MM-dd"),
        NumAdult = adults,
        NumChild = children,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        Channel = channel,
        ApiReference = apiReference,
        Price = 240m
    };

    private sealed class FakeBeds24Client : IBeds24Client
    {
        public List<Beds24Booking> Bookings { get; } = [];

        public Task<Beds24Tokens> SetupAsync(string inviteCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Beds24Tokens("access", 86400, "refresh"));

        public Task<Beds24AccessToken> GetAccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Beds24AccessToken("access", 86400));

        public Task<IReadOnlyList<Beds24Property>> GetPropertiesAsync(string accessToken, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Beds24Property>>(
            [
                new Beds24Property
                {
                    Id = 77,
                    Name = "Alpenhof",
                    RoomTypes =
                    [
                        new Beds24RoomType { Id = 11, Name = "Double Room", Units = [new Beds24Unit { Id = 501, Name = "101" }, new Beds24Unit { Id = 502, Name = "102" }] },
                        new Beds24RoomType { Id = 12, Name = "Single" },
                        new Beds24RoomType { Id = 13, Name = "Suite" }
                    ]
                }
            ]);

        public Task<IReadOnlyList<Beds24Booking>> GetBookingsAsync(string accessToken, Beds24BookingQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Beds24Booking>>(Bookings.ToList());
    }
}
