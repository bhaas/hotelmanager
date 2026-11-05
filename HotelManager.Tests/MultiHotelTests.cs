using System.Security.Claims;
using HotelManager.Web.Data;
using HotelManager.Web.Domain;
using HotelManager.Web.Features.Billing;
using HotelManager.Web.Domain.Billing;
using HotelManager.Web.Features.Hotels;
using HotelManager.Web.Features.Users;
using HotelManager.Web.Infrastructure.Identity;
using HotelManager.Web.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HotelManager.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class MultiHotelTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Every_hotel_owned_table_has_a_row_level_security_policy()
    {
        await using var scope = fixture.CreateScope(null);
        await using var db = await DatabaseFixture.CreateDbAsync(scope);
        db.BypassRowLevelSecurity = true;

        var tenantTables = db.Model.GetEntityTypes()
            .Where(entity => entity.FindProperty("HotelId") is not null)
            .Where(entity => entity.ClrType != typeof(ApplicationUser) && entity.ClrType != typeof(HotelMembership))
            .Select(entity => entity.GetTableName()!)
            .Order()
            .ToList();
        var protectedTables = await db.Database
            .SqlQueryRaw<string>("""
                SELECT c.relname AS "Value" FROM pg_class c
                JOIN pg_policies p ON p.tablename = c.relname AND p.schemaname = 'public'
                WHERE c.relrowsecurity AND p.policyname = 'tenant_isolation'
                """)
            .ToListAsync();

        Assert.NotEmpty(tenantTables);
        Assert.Equal(tenantTables, protectedTables.Order().ToList());
    }

    [Fact]
    public async Task Row_level_security_hides_other_hotels_even_without_query_filters()
    {
        var hotelA = await fixture.CreateHotelAsync("RLS A");
        var hotelB = await fixture.CreateHotelAsync("RLS B");
        foreach (var hotel in new[] { hotelA, hotelB })
        {
            await using var scope = fixture.CreateScope(hotel);
            await using var db = await DatabaseFixture.CreateDbAsync(scope);
            db.Rooms.Add(new Room { Id = Guid.NewGuid(), Number = "1", Type = "DK", Floor = 1 });
            await db.SaveChangesAsync();
        }

        await using (var scope = fixture.CreateScope(hotelA))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var visible = await db.Rooms.IgnoreQueryFilters().Select(room => room.HotelId).Distinct().ToListAsync();
            Assert.Equal([hotelA], visible);

            var exception = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
                """INSERT INTO "Rooms" ("Id", "HotelId", "Number", "Type", "Floor", "IsActive") VALUES ({0}, {1}, 'X', 'DK', 1, true)""",
                Guid.NewGuid(), hotelB));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
        }

        await using (var scope = fixture.CreateScope(null))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            Assert.Equal(0, await db.Rooms.IgnoreQueryFilters().CountAsync(room => room.HotelId == hotelA || room.HotelId == hotelB));
        }
    }

    [Fact]
    public async Task Platform_admin_creates_hotel_and_existing_account_gets_access_without_new_password()
    {
        var homeHotel = await fixture.CreateHotelAsync("Platform home");
        var owner = await CreateUserAsync(homeHotel, "owner", HotelRoles.Admin);
        await using (var scope = fixture.CreateScope(homeHotel))
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            if (!await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().RoleExistsAsync(PlatformRoles.PlatformAdmin))
            {
                await scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>().CreateAsync(new IdentityRole(PlatformRoles.PlatformAdmin));
            }

            await users.AddToRoleAsync((await users.FindByIdAsync(owner.Id))!, PlatformRoles.PlatformAdmin);
        }

        PlatformHotelCreateResult created;
        await using (var scope = CreateSignedInScope(homeHotel, owner.Id, HotelRoles.Admin, PlatformRoles.PlatformAdmin))
        {
            created = await scope.ServiceProvider.GetRequiredService<IPlatformHotelService>().CreateAsync(
                new PlatformHotelCreateRequest($"Second {Guid.NewGuid():N}", "Innsbruck", "Austria", owner.Email!, "Owner", SeedDemoData: true));
        }

        Assert.Null(created.AdminPassword);
        Assert.Equal(1, created.Hotel.MemberCount);

        await using (var scope = fixture.CreateScope(created.Hotel.Id))
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            Assert.NotEmpty(await db.Rooms.ToListAsync());
        }

        await using (var scope = fixture.CreateScope(null))
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(owner.Id))!;
            var hotels = await scope.ServiceProvider.GetRequiredService<HotelAccessService>().GetAccessibleHotelsAsync(user);
            Assert.Contains(hotels, hotel => hotel.HotelId == homeHotel);
            Assert.Contains(hotels, hotel => hotel.HotelId == created.Hotel.Id && hotel.Role == HotelRoles.Admin);
        }
    }

    [Fact]
    public async Task Claims_follow_the_selected_hotel_and_its_membership_role()
    {
        var hotelA = await fixture.CreateHotelAsync("Claims A");
        var hotelB = await fixture.CreateHotelAsync("Claims B");
        var member = await CreateUserAsync(hotelA, "claims", HotelRoles.Admin);
        await AddMembershipAsync(member.Id, hotelB, HotelRoles.FrontDesk);

        await using var scope = fixture.CreateScope(null);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var factory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>();
        var user = (await users.FindByIdAsync(member.Id))!;

        var principalA = await factory.CreateAsync(user);
        Assert.Equal(hotelA.ToString(), principalA.FindFirstValue(HotelClaimTypes.HotelId));
        Assert.True(principalA.IsInRole(HotelRoles.Admin));

        user.HotelId = hotelB;
        await users.UpdateAsync(user);
        var principalB = await factory.CreateAsync(user);
        Assert.Equal(hotelB.ToString(), principalB.FindFirstValue(HotelClaimTypes.HotelId));
        Assert.True(principalB.IsInRole(HotelRoles.FrontDesk));
        Assert.False(principalB.IsInRole(HotelRoles.Admin));
    }

    [Fact]
    public async Task Adding_an_existing_account_and_deactivating_it_only_affects_this_hotel()
    {
        var hotelA = await fixture.CreateHotelAsync("Users A");
        var hotelB = await fixture.CreateHotelAsync("Users B");
        var adminB = await CreateUserAsync(hotelB, "admin-b", HotelRoles.Admin);
        var shared = await CreateUserAsync(hotelA, "shared", HotelRoles.Manager);

        await using (var scope = CreateSignedInScope(hotelB, adminB.Id, HotelRoles.Admin))
        {
            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            var added = await administration.CreateAsync(new UserCreateRequest(shared.Email!, "Ignored", HotelRoles.FrontDesk));
            Assert.Null(added.Password);
            Assert.Equal(1, added.User.OtherHotelCount);
            Assert.False(added.User.CanManageAccount);

            await Assert.ThrowsAsync<UserAdministrationException>(() => administration.ResetPasswordAsync(shared.Id));
            var deactivated = await administration.SetLockedAsync(shared.Id, locked: true);
            Assert.True(deactivated.IsLocked);
        }

        await using (var scope = fixture.CreateScope(null))
        {
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(shared.Id))!;
            var hotels = await scope.ServiceProvider.GetRequiredService<HotelAccessService>().GetAccessibleHotelsAsync(user);
            Assert.Equal([hotelA], hotels.Select(hotel => hotel.HotelId));
        }
    }

    [Fact]
    public async Task Users_without_an_active_hotel_cannot_sign_in()
    {
        var hotel = await fixture.CreateHotelAsync("Sign-in check");
        var member = await CreateUserAsync(hotel, "signin", HotelRoles.FrontDesk);

        await using var scope = fixture.CreateScope(null);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<ApplicationUser>>();
        Assert.True(await signIn.CanSignInAsync((await users.FindByIdAsync(member.Id))!));

        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var target = await db.Hotels.SingleAsync(item => item.Id == hotel);
            target.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.False(await signIn.CanSignInAsync((await users.FindByIdAsync(member.Id))!));
    }

    [Fact]
    public async Task Admins_assign_users_to_the_other_hotels_they_administer()
    {
        var hotelA = await fixture.CreateHotelAsync("Group A");
        var hotelB = await fixture.CreateHotelAsync("Group B");
        var hotelC = await fixture.CreateHotelAsync("Group C");
        var owner = await CreateUserAsync(hotelA, "group-owner", HotelRoles.Admin);
        await AddMembershipAsync(owner.Id, hotelB, HotelRoles.Admin);
        var member = await CreateUserAsync(hotelA, "group-member", HotelRoles.FrontDesk);

        await using (var scope = CreateSignedInScope(hotelA, owner.Id, HotelRoles.Admin))
        {
            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            var options = await administration.GetOtherHotelsAsync(member.Id);
            var b = Assert.Single(options);
            Assert.Equal((hotelB, true, false), (b.HotelId, b.CanEdit, b.HasAccess));

            var summary = await administration.SetOtherHotelsAsync(member.Id, [new UserHotelAssignment(hotelB, HotelRoles.Manager)]);
            Assert.Equal(1, summary.OtherHotelCount);
            Assert.True(summary.CanManageAccount);

            await Assert.ThrowsAsync<UserAdministrationException>(() =>
                administration.SetOtherHotelsAsync(member.Id, [new UserHotelAssignment(hotelC, HotelRoles.FrontDesk)]));
            await Assert.ThrowsAsync<UserAdministrationException>(() =>
                administration.SetOtherHotelsAsync(owner.Id, [new UserHotelAssignment(hotelB, HotelRoles.Manager)]));
        }

        Assert.Equal(
            [(hotelA, HotelRoles.FrontDesk), (hotelB, HotelRoles.Manager)],
            (await GetAccessibleHotelsAsync(member.Id)).Select(hotel => (hotel.HotelId, hotel.Role)).OrderBy(item => item.HotelId == hotelB));

        await using (var scope = CreateSignedInScope(hotelA, owner.Id, HotelRoles.Admin))
        {
            var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
            await administration.SetOtherHotelsAsync(member.Id, [new UserHotelAssignment(hotelB, null)]);
            Assert.False(Assert.Single(await administration.GetOtherHotelsAsync(member.Id)).HasAccess);
        }

        Assert.Equal([hotelA], (await GetAccessibleHotelsAsync(member.Id)).Select(hotel => hotel.HotelId));
    }

    private async Task<IReadOnlyList<AccessibleHotel>> GetAccessibleHotelsAsync(string userId)
    {
        await using var scope = fixture.CreateScope(null);
        var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId))!;
        return await scope.ServiceProvider.GetRequiredService<HotelAccessService>().GetAccessibleHotelsAsync(user);
    }

    [Fact]
    public async Task Hotel_operating_settings_drive_invoice_numbers_footer_and_cash_limit()
    {
        var hotel = await fixture.CreateHotelAsync("Settings hotel");
        var admin = await CreateUserAsync(hotel, "settings-admin", HotelRoles.Admin);
        await using var scope = CreateSignedInScope(hotel, admin.Id, HotelRoles.Admin);
        var profiles = scope.ServiceProvider.GetRequiredService<IHotelProfileService>();
        var current = await profiles.GetCurrentAsync();
        Assert.Equal(HotelOperatingSettings.Default, current.Operations);

        var settings = new HotelOperatingSettings("wien", 30, 500m, new TimeOnly(14, 0), new TimeOnly(10, 30), "IBAN AT00 1234");
        var updated = await profiles.UpdateCurrentAsync(new HotelProfileUpdate(
            "Renamed hotel", null, null, null, null, null, null, null, null, current.TimeZone, settings));
        Assert.Equal("Renamed hotel", updated.Name);
        Assert.Equal(settings with { InvoiceNumberPrefix = "WIEN" }, updated.Operations);
        await Assert.ThrowsAsync<HotelValidationException>(() => profiles.UpdateCurrentAsync(new HotelProfileUpdate(
            "Renamed hotel", null, null, null, null, null, null, null, null, current.TimeZone, settings with { InvoiceNumberPrefix = "A/B" })));

        var reservationId = Guid.NewGuid();
        await using (var db = await DatabaseFixture.CreateDbAsync(scope))
        {
            var room = new Room { Id = Guid.NewGuid(), Number = "1", Type = "DK" };
            db.Rooms.Add(room);
            db.Reservations.Add(new Reservation
            {
                Id = reservationId, RoomId = room.Id, GuestName = "Guest", Status = ReservationStatus.Confirmed,
                CheckIn = new DateOnly(2026, 12, 1), CheckOut = new DateOnly(2026, 12, 3)
            });
            await db.SaveChangesAsync();
        }

        var billing = scope.ServiceProvider.GetRequiredService<IBillingService>();
        await billing.AddFolioEntryAsync(reservationId, new BillingAddFolioEntryRequest(
            "Room", FolioEntryType.Charge, 2, 500m, 10m, new DateOnly(2026, 12, 1), null), CancellationToken.None);
        await Assert.ThrowsAsync<BillingValidationException>(() => billing.RegisterPaymentAsync(
            reservationId, new BillingRegisterPaymentRequest(501m, PaymentMethod.Cash, null, null, null), CancellationToken.None));
        await billing.RegisterPaymentAsync(
            reservationId, new BillingRegisterPaymentRequest(501m, PaymentMethod.Card, null, null, null), CancellationToken.None);

        var invoice = await billing.IssueInvoiceAsync(
            reservationId, new BillingIssueInvoiceRequest("Guest", "Street 1", null, new DateOnly(2027, 1, 1)), CancellationToken.None);
        Assert.Matches(@"^WIEN-\d{4}-000001$", invoice.InvoiceNumber);
        Assert.Equal("IBAN AT00 1234", invoice.FooterText);
        Assert.Equal("Renamed hotel", invoice.SellerName);
    }

    private AsyncServiceScope CreateSignedInScope(Guid hotelId, string userId, params string[] roles)
    {
        var scope = fixture.CreateScope(hotelId);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId), new(HotelClaimTypes.HotelId, hotelId.ToString()) };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        scope.ServiceProvider.GetRequiredService<TenantContext>().SetUser(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")));
        return scope;
    }

    private async Task<ApplicationUser> CreateUserAsync(Guid hotelId, string prefix, string role)
    {
        await using var scope = fixture.CreateScope(hotelId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{prefix}-{Guid.NewGuid():N}@test.local";
        var user = new ApplicationUser { HotelId = hotelId, UserName = email, Email = email, DisplayName = prefix };
        var result = await users.CreateAsync(user, "Test-Password-123");
        Assert.True(result.Succeeded, string.Join(" ", result.Errors.Select(error => error.Description)));
        await AddMembershipAsync(user.Id, hotelId, role);
        return user;
    }

    private async Task AddMembershipAsync(string userId, Guid hotelId, string role)
    {
        await using var scope = fixture.CreateScope(hotelId);
        await using var db = await DatabaseFixture.CreateDbAsync(scope);
        db.HotelMemberships.Add(new HotelMembership { UserId = userId, HotelId = hotelId, Role = role });
        await db.SaveChangesAsync();
    }
}
