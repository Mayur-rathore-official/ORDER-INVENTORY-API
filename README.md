# Order & Inventory API

An online store backend where customers place orders and stock always stays accurate.
Built with ASP.NET Core Web API (.NET 10), Entity Framework Core and SQL Server.

## Table of contents
1. [Tech stack](#tech-stack)
2. [Architecture overview](#architecture-overview)
3. [Setup and run](#setup-and-run)
4. [Migrations](#migrations)
5. [Running tests](#running-tests)
6. [Seeded login credentials](#seeded-login-credentials)
7. [API endpoints](#api-endpoints)
8. [How concurrency is solved](#how-concurrency-is-solved)
9. [How idempotency is solved](#how-idempotency-is-solved)
10. [Other business rules](#other-business-rules)
11. [Part B: SQL and generated LINQ query](#part-b-sql-and-generated-linq-query)
12. [Assumptions](#assumptions)
13. [Status: done and not done](#status-done-and-not-done)
14. [What I would improve with more time](#what-i-would-improve-with-more-time)

## Tech stack
- Backend: C#, ASP.NET Core Web API (.NET 10), EF Core (Code-First)
- Database: SQL Server (LocalDB for development)
- Auth: JWT access tokens plus refresh tokens, role-based authorization
- Logging: Serilog (structured)
- API docs: Swagger with JWT support
- Tests: xUnit, Moq, `WebApplicationFactory`
- Frontend: <<Angular / React, or "not completed, see Status">>

## Architecture overview
Layered (clean) architecture. Dependencies point inward, toward the Domain.

```
src/
├── OrderInventory.Domain          Entities, enums, domain exceptions. No dependencies.
├── OrderInventory.Application     DTOs, service interfaces, business logic
│                                  (OrderService, DiscountCalculator, OrderStatusRules), validators.
├── OrderInventory.Infrastructure  EF Core DbContext, migrations, seeding, JWT/token service,
│                                  background service, DI registration.
└── OrderInventory.Api             Controllers, middleware (global exception handler),
                                   rate limiting, Swagger, Program.cs.
tests/
├── OrderInventory.UnitTests         Rules 1, 3, 4, 5
└── OrderInventory.IntegrationTests  Rule 2 (concurrency) against real SQL Server
```

Key design decisions:
- Controllers are thin. All business rules live in the Application layer, so they are unit-testable without HTTP.
- All money values use `decimal` with `HasPrecision(18, 2)`.
- Domain exceptions (`NotFoundException`, `BusinessRuleException`, `ConflictException`, `ForbiddenException`) are mapped to HTTP status codes by one global exception handler that returns `ProblemDetails`.
- Everything is `async/await` and uses dependency injection.

## Setup and run

### Prerequisites
- .NET SDK 8 or later (developed on .NET 10)
- SQL Server or SQL Server LocalDB (installed with Visual Studio)
- `dotnet-ef` tool: `dotnet tool install --global dotnet-ef`

### Steps
```bash
git clone <your-repo-url>
cd OrderInventory

# 1. Set the JWT signing key (never committed to the repo)
cd src/OrderInventory.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Key" "<a-random-string-of-at-least-32-characters>"
cd ../..

# 2. (Optional) change the connection string in src/OrderInventory.Api/appsettings.json
#    Default: (localdb)\MSSQLLocalDB, database OrderInventoryDb

# 3. Run
dotnet run --project src/OrderInventory.Api
```

On startup the app applies pending migrations and seeds the database automatically.
Open Swagger at `https://localhost:<port>/swagger`, click **Authorize** and paste the access token returned by `/api/auth/login`.

> No secrets are stored in the repository. The JWT key comes from user-secrets or an environment variable (`Jwt__Key`).

### Docker (optional)
```bash
docker compose up --build
```
<<Keep this section only if docker-compose works. Otherwise delete it.>>

## Migrations
```bash
# Create a new migration
dotnet ef migrations add <Name> -p src/OrderInventory.Infrastructure -s src/OrderInventory.Api

# Apply to the database (also happens automatically on startup)
dotnet ef database update -p src/OrderInventory.Infrastructure -s src/OrderInventory.Api
```

## Running tests
```bash
# All tests
dotnet test

# Unit tests only
dotnet test tests/OrderInventory.UnitTests

# Integration tests only (needs SQL Server, see below)
dotnet test tests/OrderInventory.IntegrationTests
```

The integration test uses a **real SQL Server database** and not the EF in-memory provider, because `RowVersion` concurrency tokens are not enforced by the in-memory provider. The connection string is read from the `TestConnection` setting / environment variable (default: LocalDB, database `OrderInventoryTestDb`).

## Seeded login credentials
| Role     | Email                 | Password       |
|----------|-----------------------|----------------|
| Admin    | admin@store.com       | Admin@123      |
| Customer | customer1@store.com   | Customer@123   |
| Customer | customer2@store.com   | Customer@123   |
| Customer | customer3@store.com   | Customer@123   |

These are demo credentials for local development only. Seed data also includes 15 products. Product `SSD-001` has stock **1** on purpose, to demonstrate the last-unit concurrency case.

## API endpoints
| Method | Route | Access |
|--------|-------|--------|
| POST | `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh` | Public (login is rate limited) |
| GET | `/api/products`, `/api/products/{id}` | Authenticated |
| POST / PUT | `/api/products`, `/api/products/{id}` | Admin |
| POST | `/api/orders` (header `Idempotency-Key`) | Customer |
| GET | `/api/orders/my` (paged) | Customer |
| GET | `/api/orders/{id}` | Owner or Admin |
| PATCH | `/api/orders/{id}/status` | Admin; Customer may only cancel own Pending order |
| GET | `/api/reports/top-customers?days=30` | Admin |

`GET /api/products` supports server-side pagination, filtering (name, min/max price, in-stock) and sorting. All of it is translated to SQL through `IQueryable`, nothing is filtered in memory.

## How concurrency is solved
**Problem:** two customers buy the last unit at the same time. Only one may succeed.

**Solution: optimistic concurrency with a `RowVersion` column.**
1. `Product.RowVersion` is a SQL Server `rowversion` column configured with `IsRowVersion()`. SQL Server changes it on every update.
2. When an order is placed, all products are loaded and stock is decremented inside **one database transaction**. If any item has insufficient stock, an exception is thrown, the transaction rolls back and no stock changes (rule 1).
3. On `SaveChanges`, EF Core issues `UPDATE ... WHERE Id = @id AND RowVersion = @originalRowVersion`. If another request changed the product first, zero rows are affected and EF throws `DbUpdateConcurrencyException`.
4. The service catches `DbUpdateConcurrencyException`, rolls back, and throws a `ConflictException`. The global exception handler turns it into **HTTP 409 Conflict** (`ProblemDetails`), never a 500.
5. As a second safety net, the database has a check constraint `StockQuantity >= 0`, so stock can never go negative even if application code has a bug.

**Proof:** the integration test `ConcurrencyTests` fires two parallel `POST /api/orders` requests for the last unit of `SSD-001` and asserts exactly one `201 Created` and one `409 Conflict`, and that final stock is 0.

## How idempotency is solved
**Problem:** a client retries (or double-clicks) `POST /api/orders` and must not create a second order or reduce stock twice.

**Solution: `Idempotency-Key` header plus a unique database index.**
1. The client sends a unique `Idempotency-Key` header (a GUID).
2. Before creating anything, the server looks up `IdempotencyRecords` by `(UserId, Key)`. If found, it returns the **original order** and changes nothing.
3. If not found, the order and the idempotency record are saved **in the same transaction** as the stock reservation.
4. The table has a **unique index on `(UserId, Key)`**. If two identical requests race past the lookup at the same moment, the database rejects the second insert. The service catches that unique-violation, loads the original order and returns it.
5. A hash of the request body is stored. If the same key is reused with a different body, the API returns `400 Bad Request` (a key must always map to one request).
6. The key is scoped per user, so one user cannot read or collide with another user's keys.

## Other business rules
- **Status transitions (rule 4):** Pending to Confirmed to Shipped; Pending or Confirmed to Cancelled. Shipped and Cancelled are final. Invalid transitions return `400` with a clear message. Cancelling restores stock in the same transaction. Logic lives in `OrderStatusRules`.
- **Discounts (rule 5):** calculated only on the server in `DiscountCalculator`: 5% when SubTotal is above 10,000; an extra 10% on every 5th order of a customer. Uses `decimal` and `Math.Round(..., 2)`.
- **Background cancellation (rule 6):** a `BackgroundService` runs periodically, cancels orders Pending for more than 30 minutes and restores stock.
- **Rate limiting:** built-in .NET rate limiter, 5 login attempts per minute per IP, returns `429`.
- **Caching:** product list cached with `IMemoryCache`, invalidated when a product is created or updated.
- **Errors:** global handler returns `ProblemDetails` with correct status codes (400, 401, 403, 404, 409, 429).

## Part B: SQL and generated LINQ query
- The five SQL queries are in [`QUERIES.sql`](QUERIES.sql).
- Query 1 is implemented as `GET /api/reports/top-customers` using LINQ in a single query (no N+1, nothing loaded into memory).

Generated SQL taken from the application logs:

```sql
<<Run the endpoint, copy the SQL that EF Core logs
  (look for "Executed DbCommand" in the console) and paste it here.>>
```

## Assumptions
- A customer can only place orders for active products (`IsActive = true`).
- Order quantity must be at least 1; duplicate product lines in one order are merged.
- "Every 5th order" counts all of the customer's previous non-cancelled orders; the 5th, 10th, 15th... order gets the extra 10%. The 10% is applied on top of the 5% discount, in sequence.
- The 5% rule uses SubTotal strictly greater than 10,000.
- Registration always creates a `Customer`. Admins exist only through seeding.
- Refresh tokens are stored hashed, expire after <<7>> days, and are rotated on each use.
- `Idempotency-Key` is required on `POST /api/orders`.
- Dates are stored in UTC.
- <<Add any other assumption you made.>>

## Status: done and not done
| Item | Status |
|------|--------|
| Part A rules 1 to 4 | <<Done / Partial>> |
| Part A rules 5 and 6 | <<Done / Partial>> |
| JWT + refresh tokens, roles, rate limiting, caching, pagination | <<Done / Partial>> |
| Unit tests (10+) | <<Done / Partial>> |
| Integration test (rule 2) | <<Done / Partial>> |
| Part B (QUERIES.sql + LINQ endpoint) | <<Done / Partial>> |
| Part C (REVIEW.md) | <<Done / Partial>> |
| Part D (frontend) | <<Done / Not done>> |
| Bonus (Docker, SignalR, Outbox, CI, frontend tests) | <<list what is done>> |

Not completed: <<list honestly what is left>>

## What I would improve with more time
- Move idempotency records to a table with automatic expiry (cleanup job) and also cache the stored response body.
- Add the **Outbox pattern** for "order placed" notifications so messages are never lost if the app crashes after commit.
- Add **SignalR** so customers see order status changes live.
- Replace `IMemoryCache` with Redis (distributed cache) for multi-instance deployments, and tag-based invalidation.
- Add retry with backoff on transient SQL errors and deadlock handling.
- Per-product row locking (`UPDLOCK`) as an alternative strategy for hot products with very high contention.
- More tests: refresh-token rotation, rate limiting, authorization (403) paths, and pagination edge cases.
- Add health checks, metrics and OpenTelemetry tracing.
- GitHub Actions pipeline: build, test, and publish a Docker image.
- Refresh tokens in HttpOnly cookies for the frontend instead of local storage.