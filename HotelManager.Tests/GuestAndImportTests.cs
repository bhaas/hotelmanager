using System.Text;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Guests;
using HotelManager.Web.Features.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelManager.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class GuestAndImportTests(DatabaseFixture fixture)
{
    private const string Header = "Book number,Booked by,Guest name(s),Check-in,Check-out,Status,Rooms,Persons,Price,Commission %,Commission amount,Remarks,Booker country,Unit type,Email";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Erasing_a_guest_anonymizes_stays_deletes_profiles_without_stays_and_refuses_upcoming_stays()
    {
        var hotelId = await fixture.CreateHotelAsync("GDPR hotel");
        Guid pastGuest, newGuest, upcomingGuest;
        await using (var scope = fixture.CreateScope(hotelId))
        {
            var guests = scope.ServiceProvider.GetRequiredService<IGuestService>();
            pastGuest = (await guests.CreateAsync(new GuestUpsertRequest("Erika", "Muster", Email: "erika@example.com", Phone: "+49 1"))).Id;
            newGuest = (await guests.CreateAsync(new GuestUpsertRequest(null, "Nobody"))).Id;
            upcomingGuest = (await guests.CreateAsync(new GuestUpsertRequest("Ute", "Future"))).Id;

            await using var db = await DatabaseFixture.CreateDbAsync(scope);
            var room = new Room { Id = Guid.NewGuid(), Number = "101", Type = "Double" };
            db.Rooms.Add(room);
            db.Reservations.AddRange(
                Stay(room.Id, pastGuest, "Erika Muster", Today.AddDays(-10), Today.AddDays(-8), ReservationStatus.CheckedOut, "Allergic to nuts"),
                Stay(room.Id, upcomingGuest, "Ute Future", Today.AddDays(5), Today.AddDays(7), ReservationStatus.Confirmed, null));
            await db.SaveChangesAsync();

            var export = await guests.ExportAsync(pastGuest);
            var json = Encoding.UTF8.GetString(export.Content);
            Assert.Contains("erika@example.com", json);
            Assert.Contains("Allergic to nuts", json);

            Assert.Equal(GuestErasureResult.Anonymized, await guests.EraseAsync(pastGuest));
            Assert.Equal(GuestErasureResult.Deleted, await guests.EraseAsync(newGuest));
            await Assert.ThrowsAsync<GuestValidationException>(() => guests.EraseAsync(upcomingGuest));
        }

        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var anonymized = await db.Guests.Include(guest => guest.Reservations).SingleAsync(guest => guest.Id == pastGuest);
            Assert.Equal(GuestService.AnonymizedName, anonymized.LastName);
            Assert.Null(anonymized.FirstName);
            Assert.Null(anonymized.Email);
            Assert.Null(anonymized.Phone);
            Assert.NotNull(anonymized.AnonymizedAtUtc);
            var stay = Assert.Single(anonymized.Reservations);
            Assert.Equal(GuestService.AnonymizedName, stay.GuestName);
            Assert.Null(stay.Notes);

            Assert.False(await db.Guests.AnyAsync(guest => guest.Id == newGuest));
            Assert.Equal("Future", (await db.Guests.SingleAsync(guest => guest.Id == upcomingGuest)).LastName);
        }
    }

    [Fact]
    public void Csv_reader_detects_semicolons_and_handles_quotes_and_line_breaks()
    {
        var csv = "\uFEFFBuchungsnummer;Gastname;Bemerkungen\r\n123;\"Müller; Hans\";\"Late arrival\r\n\"\"VIP\"\"\"\r\n\r\n456;Schmidt;\r\n";

        var table = ImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "export.csv");

        Assert.Equal(["Buchungsnummer", "Gastname", "Bemerkungen"], table.Headers);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("Müller; Hans", table.Rows[0][1]);
        Assert.Equal("Late arrival\r\n\"VIP\"", table.Rows[0][2]);
        Assert.Equal("Schmidt", table.Rows[1][1]);
    }

    [Fact]
    public async Task Booking_com_import_creates_reservations_and_reimport_updates_and_cancels()
    {
        var hotelId = await fixture.CreateHotelAsync("Import hotel");
        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            db.RoomCategories.Add(new RoomCategory { Id = Guid.NewGuid(), Code = "DBL", Name = "Double", MaxOccupancy = 2 });
            db.Rooms.AddRange(
                new Room { Id = Guid.NewGuid(), Number = "201", Type = "Double" },
                new Room { Id = Guid.NewGuid(), Number = "202", Type = "Double" },
                new Room { Id = Guid.NewGuid(), Number = "203", Type = "Double" });
            await db.SaveChangesAsync();
        }

        var first = Csv(
            Row("1001", "Anna Gast", Today.AddDays(10), Today.AddDays(12), "ok", 1, 2, "Double Room", "anna@example.com"),
            Row("1002", "Group Lead", Today.AddDays(10), Today.AddDays(11), "ok", 2, 4, "Double Room, Double Room", null),
            Row("1003", "Cancelled Early", Today.AddDays(20), Today.AddDays(21), "cancelled_by_guest", 1, 1, "Double Room", null),
            Row("1004", "Old Stay", Today.AddDays(-5), Today.AddDays(-3), "ok", 1, 1, "Double Room", null));

        await using (var scope = fixture.CreateScope(hotelId))
        {
            var importer = scope.ServiceProvider.GetRequiredService<IBookingComImportService>();
            var options = await OptionsAsync(importer, first);
            Assert.Equal("Double", options.UnitTypeCategories["Double Room"]);

            var preview = await importer.PreviewAsync(first, options);
            Assert.Equal(3, preview.Count(BookingComImportAction.Create));
            Assert.Equal(2, preview.Count(BookingComImportAction.Skip));
            Assert.Equal(3, preview.Rows.Where(row => row.Action == BookingComImportAction.Create).Select(row => row.RoomNumber).Distinct().Count());

            var result = await importer.ImportAsync(first, options);
            Assert.Equal(3, result.Count(BookingComImportAction.Create));

            var reimport = await importer.ImportAsync(first, options);
            Assert.Equal(0, reimport.Count(BookingComImportAction.Create) + reimport.Count(BookingComImportAction.Update));
            Assert.Equal(3, reimport.Count(BookingComImportAction.Unchanged));

            // The fourth booking does not fit: all three rooms are taken on those nights.
            var overbooked = Csv(Row("1005", "No Room", Today.AddDays(10), Today.AddDays(11), "ok", 1, 1, "Double Room", null));
            var overbookedResult = await importer.ImportAsync(overbooked, await OptionsAsync(importer, overbooked));
            Assert.Equal(BookingComImportAction.Error, Assert.Single(overbookedResult.Rows).Action);

            var second = Csv(
                Row("1001", "Anna Gast", Today.AddDays(11), Today.AddDays(13), "ok", 1, 2, "Double Room", "anna@example.com"),
                Row("1002", "Group Lead", Today.AddDays(10), Today.AddDays(11), "cancelled_by_guest", 2, 4, "Double Room, Double Room", null));
            var update = await importer.ImportAsync(second, await OptionsAsync(importer, second));
            Assert.Equal(1, update.Count(BookingComImportAction.Update));
            Assert.Equal(2, update.Count(BookingComImportAction.Cancel));
        }

        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var reservations = await db.Reservations.Include(reservation => reservation.Guest).OrderBy(reservation => reservation.ExternalReference).ToListAsync();
            Assert.Equal(["1001", "1002", "1002-2"], reservations.Select(reservation => reservation.ExternalReference));
            Assert.All(reservations, reservation => Assert.Equal(ReservationSource.BookingCom, reservation.Source));

            var anna = reservations[0];
            Assert.Equal((Today.AddDays(11), Today.AddDays(13)), (anna.CheckIn, anna.CheckOut));
            Assert.Equal(ReservationStatus.Confirmed, anna.Status);
            Assert.Equal("anna@example.com", anna.Guest?.Email);
            Assert.Contains("Booking.com 1001", anna.Notes);

            Assert.All(reservations.Skip(1), reservation => Assert.Equal(ReservationStatus.Cancelled, reservation.Status));
            Assert.Equal(4, reservations.Skip(1).Sum(reservation => reservation.Occupancy));
            Assert.Single(reservations.Skip(1).Select(reservation => reservation.GuestId).Distinct());
        }
    }

    private static Reservation Stay(Guid roomId, Guid guestId, string name, DateOnly checkIn, DateOnly checkOut, ReservationStatus status, string? notes) => new()
    {
        Id = Guid.NewGuid(),
        RoomId = roomId,
        GuestId = guestId,
        GuestName = name,
        CheckIn = checkIn,
        CheckOut = checkOut,
        Status = status,
        Notes = notes
    };

    private static async Task<BookingComImportOptions> OptionsAsync(IBookingComImportService importer, ImportTable table)
    {
        var columns = importer.DetectColumns(table.Headers);
        var categories = await importer.SuggestCategoriesAsync(table, columns);
        return new BookingComImportOptions(columns, categories);
    }

    private static string Row(string number, string guest, DateOnly checkIn, DateOnly checkOut, string status, int rooms, int persons, string unitType, string? email) =>
        $"{number},{guest},{guest},{checkIn:yyyy-MM-dd},{checkOut:yyyy-MM-dd},{status},{rooms},{persons},240 EUR,15,36 EUR,,de,\"{unitType}\",{email}";

    private static ImportTable Csv(params string[] rows) =>
        ImportFileReader.Read(Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows])), "bookings.csv");
}
