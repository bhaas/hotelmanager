using System.Text;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelManager.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class FeratelGuestImportTests(DatabaseFixture fixture)
{
    private const string Header = "Titel;Anrede;Vorname;Nachname;Straße;Straße 2;Nationalität;PLZ;Stadt/Ort;Geburtsdatum;Reisedokument Nr. / Ausstell.Datum / Behörde;Land;Position;Telefon;Fax;E-Mail;Internet;Firma;Anz. Aufenthalte;Kommentar;Nutzung Marketing;Reisemotiv;Empfehlungsart;Anreisetyp;Kfz-Kennzeichen;Gast-Interessen;Sprache";

    [Fact]
    public async Task Feratel_export_creates_merges_and_updates_guests_and_reimport_is_unchanged()
    {
        var hotelId = await fixture.CreateHotelAsync("Feratel hotel");
        Guid bookingGuestId;
        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var bookingGuest = new Guest { Id = Guid.NewGuid(), FirstName = "Jana", LastName = "Novak", Email = "old@example.com", MarketingConsent = true };
            db.Guests.Add(bookingGuest);
            await db.SaveChangesAsync();
            bookingGuestId = bookingGuest.Id;
        }

        var table = Table(
            ";Herr;Hans;Müller;Dorfstraße 1;;DE;80331;München;01.02.1970;C123, Stadt München 01.01.2020;Deutschland;;+49 89 1;;family@example.com;;;1;;ja;Urlaub;Internet;Auto;M-AB 123;Wandern;Deutsch",
            ";Frau;Eva;Müller;Dorfstraße 1;;DE;80331;München;03.04.1972;;Deutschland;;;;family@example.com;;;1;;nein;;;;;;Deutsch",
            ";Frau;Jana;Novak;Hlavní 5;;CZ;11000;Praha;05.06.1985;;Tschechien;;;;jana@example.com;;;2;;nein;;;;;;Tschechisch",
            ";Herr;Hans;Müller;Dorfstraße 1;;DE;80331;München;01.02.1970;;Deutschland;;+49 89 2;;family@example.com;;;2;;ja;;;;;;Deutsch",
            ";;Ohne;;;;;;;;;;;;;;;;;;;;;;;;");

        await using (var scope = fixture.CreateScope(hotelId))
        {
            var importer = scope.ServiceProvider.GetRequiredService<IFeratelGuestImportService>();
            var preview = await importer.PreviewAsync(table);
            Assert.Equal(
                [FeratelGuestImportAction.Create, FeratelGuestImportAction.Create, FeratelGuestImportAction.Update, FeratelGuestImportAction.Unchanged, FeratelGuestImportAction.Error],
                preview.Rows.Select(row => row.Action));
            Assert.Contains("2", preview.Rows[3].Message);

            await using (var db = await DatabaseFixture.CreateDbAsync(scope))
            {
                Assert.Equal(1, await db.Guests.CountAsync());
            }

            await importer.ImportAsync(table);
        }

        await using (var scope = fixture.CreateScope(hotelId))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var guests = await db.Guests.ToListAsync();
            Assert.Equal(3, guests.Count);

            var hans = guests.Single(guest => guest.FirstName == "Hans");
            Assert.Equal(new DateOnly(1970, 2, 1), hans.DateOfBirth);
            Assert.Equal("de", hans.Language);
            Assert.Equal("C123, Stadt München 01.01.2020", hans.TravelDocument);
            Assert.Equal("M-AB 123", hans.LicensePlate);
            Assert.Equal("+49 89 2", hans.Phone);
            Assert.Equal("Wandern", hans.Interests);
            Assert.True(hans.MarketingConsent);

            var jana = guests.Single(guest => guest.Id == bookingGuestId);
            Assert.Equal(new DateOnly(1985, 6, 5), jana.DateOfBirth);
            Assert.Equal("jana@example.com", jana.Email);
            Assert.Equal("cs", jana.Language);
            Assert.Equal("Praha", jana.City);
            Assert.False(jana.MarketingConsent);
        }

        await using (var scope = fixture.CreateScope(hotelId))
        {
            var reimport = await scope.ServiceProvider.GetRequiredService<IFeratelGuestImportService>().PreviewAsync(table);
            Assert.Equal(0, reimport.ChangeCount);
            Assert.Equal(4, reimport.Count(FeratelGuestImportAction.Unchanged));
        }
    }

    [Fact]
    public async Task Files_without_feratel_columns_are_rejected()
    {
        var hotelId = await fixture.CreateHotelAsync("Feratel reject hotel");
        var table = ImportFileReader.Read(Encoding.UTF8.GetBytes("Book number;Guest name\r\n1;Test\r\n"), "export.csv");

        await using var scope = fixture.CreateScope(hotelId);
        var importer = scope.ServiceProvider.GetRequiredService<IFeratelGuestImportService>();
        await Assert.ThrowsAsync<ImportFileException>(() => importer.PreviewAsync(table));
    }

    private static ImportTable Table(params string[] rows) =>
        ImportFileReader.Read(Encoding.UTF8.GetBytes(string.Join("\r\n", [Header, .. rows])), "Export_Detailed.csv");
}
