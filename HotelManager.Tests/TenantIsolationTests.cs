using HotelManager.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace HotelManager.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class TenantIsolationTests(DatabaseFixture fixture)
{
    private static Room NewRoom(string number) => new() { Id = Guid.NewGuid(), Number = number, Type = "DK", Floor = 1 };

    [Fact]
    public async Task Hotels_can_reuse_room_numbers_and_only_see_their_own_rooms()
    {
        var hotelA = await fixture.CreateHotelAsync("Hotel A");
        var hotelB = await fixture.CreateHotelAsync("Hotel B");

        foreach (var hotel in new[] { hotelA, hotelB })
        {
            await using var scope = fixture.CreateScope(hotel);
            await using var db = await DatabaseFixture.CreateDbAsync(scope);
            db.Rooms.Add(NewRoom("101"));
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope(hotelA))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var rooms = await db.Rooms.ToListAsync();
            Assert.Single(rooms);
            Assert.Equal(hotelA, rooms[0].HotelId);
        }

        await using (var scope = fixture.CreateScope(hotelB))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            Assert.Equal(hotelB, (await db.Rooms.SingleAsync()).HotelId);
        }
    }

    [Fact]
    public async Task Saving_another_hotels_entity_is_rejected()
    {
        var hotelA = await fixture.CreateHotelAsync("Owner");
        var hotelB = await fixture.CreateHotelAsync("Intruder");
        var room = NewRoom("201");

        await using (var scope = fixture.CreateScope(hotelA))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope(hotelB))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            room.Notes = "tampered";
            db.Rooms.Update(room);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

            db.ChangeTracker.Clear();
            var foreign = NewRoom("202");
            foreign.HotelId = hotelA;
            db.Rooms.Add(foreign);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Without_tenant_queries_are_empty_and_writes_fail()
    {
        var hotel = await fixture.CreateHotelAsync("Tenantless check");
        await using (var scope = fixture.CreateScope(hotel))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            db.Rooms.Add(NewRoom("301"));
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope(null))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            Assert.Empty(await db.Rooms.ToListAsync());
            db.Rooms.Add(NewRoom("302"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
    }
}
