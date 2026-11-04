# HotelManager

Casablanca-style hotel property management system (room planner, rates & availability, billing, housekeeping, room inventory, users & roles) built with **.NET 10 Blazor Server** and **PostgreSQL**. Multi-tenant: every hotel's data is isolated by `HotelId`.

## Local development

Requirements: .NET 10 SDK, PostgreSQL 15+.

```powershell
dotnet user-secrets --project HotelManager.Web set "ConnectionStrings:HotelDb" "Host=localhost;Port=5432;Database=hotelmanager_dev;Username=postgres;Password=<your password>"
dotnet run --project HotelManager.Web
```

On first start the database is migrated, a demo hotel is created and an admin user
(`admin@hotelmanager.local`) is created. The generated password is printed **once** in the log
(or set `Bootstrap:AdminPassword` beforehand).

Alternatively with Docker: `docker compose up --build` → http://localhost:8080.

### Schema changes

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project HotelManager.Web --output-dir Data/Migrations
```

Migrations are applied automatically on startup (`Database:MigrateOnStartup`).

### Tests

```powershell
dotnet test   # uses HOTELMANAGER_TEST_DB or the web project's user-secrets; creates & drops a temporary database
```

## Cloud hosting (free tier)

Vercel cannot run a long-lived ASP.NET Core/Blazor Server (SignalR) process, so the app runs as a Docker
web service on **Render** and the database is **Neon Postgres created through Vercel**.

1. **Database (Vercel → Neon)**: Vercel dashboard → *Storage* → *Create Database* → *Neon (Postgres)*, region *Frankfurt (eu-central-1)*.
   Open the database → *.env.local* tab and copy `DATABASE_URL_UNPOOLED`
   (the direct, non-PgBouncer URL – required for migrations).
2. **App (Render)**: https://dashboard.render.com → *New* → *Blueprint* → select `bhaas/hotelmanager`.
   Render reads `render.yaml` and asks for:
   - `DATABASE_URL` – the Neon URL from step 1 (`postgresql://…?sslmode=require` is accepted as-is)
   - `Bootstrap__AdminEmail`, `Bootstrap__AdminPassword` – the first admin login (min. 10 characters)
3. Deploys run automatically after the GitHub Actions CI on `main` succeeded (`autoDeployTrigger: checksPass`).
4. Sign in and create further users under *Settings → Users & roles*.

Notes:
- The Render free instance sleeps after 15 minutes without traffic; the first request then takes ~1 minute.
- Data-protection keys (auth cookies) are stored in the database, so sign-ins survive restarts and redeploys.
- Supported connection settings (first match wins): `ConnectionStrings__HotelDb`, `HOTEL_DB_CONNECTION`, `DATABASE_URL`, `POSTGRES_URL`.
- Health check: `GET /health`.

## Roles

| Role | Access |
|------|--------|
| Admin | everything incl. users & roles |
| Manager | front office + rooms, rates & availability settings |
| FrontDesk | room planner, reservations, billing |
| Housekeeping | home (housekeeping module to follow) |

Roles are assigned **per hotel** (a user can be a member of several hotels with different roles).
**PlatformAdmin** is a global role that can create, open and deactivate any hotel (*Platform → All hotels*).
On startup the `Bootstrap:AdminEmail` user (or the oldest admin) is promoted to PlatformAdmin if none exists.

## Multiple hotels

All hotels share one database; every hotel-owned table carries a `HotelId`.
Isolation is enforced twice:

1. EF Core global query filters on the current hotel (from the `hotel_id` claim).
2. PostgreSQL **row-level security**: the app switches each connection to the `hotelmanager_app` role
   (`NOBYPASSRLS`, created by the `MultiHotel` migration) and sets `app.hotel_id`; the `tenant_isolation`
   policy hides and blocks rows of other hotels.

Requirements and notes:
- Use a **session-mode / unpooled** connection (Neon `DATABASE_URL_UNPOOLED`, not PgBouncer) so `SET ROLE` persists per connection.
- Migrations and test database setup run with `HotelDbContext.BypassRowLevelSecurity = true`.
- Switch hotels via the hotel name in the header; edit hotel data (address, VAT ID, invoice prefix, time zone) under *Settings → Hotel profile*.
