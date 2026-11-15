# Anitime — Architecture & Project Summary

> Lean **modular monolith** backend for a habit / productivity app.
> .NET 9, Clean Architecture, PostgreSQL, JWT, RabbitMQ, Redis, MinIO, Docker Compose.

---

## 1. What the system does

A mobile-first backend (JWT, CORS for Capacitor/Ionic clients) covering:

- **Auth** — signup, login, refresh tokens, forgot/reset password by emailed 6-digit code (PBKDF2 hashing). Email goes through `IEmailOutbox` (background queue) to `IEmailSender` (`Infrastructure/Email`: Resend, a Development log sender, or a no-op that warns).
- **Profile** — name, gender, DOB, age; profile + background images (MinIO); background style settings (transparent/solid/gradient/image).
- **Habits** — 30-day habit challenges with daily check toggling, current/best streaks, progress.
- **Tasks** — task list with checklist items, completion tracking.
- **Notes** — rich-text notes. Document version 1 (`blocks`, legacy) and version 2 (ProseMirror-shaped `content`, allow-listed by `Application/Rules/RichNoteV2Schema.cs`). See §10 "Notes v2".
- **Reminders** — scheduled reminders → RabbitMQ event → push notification pipeline.
- **Calendar / Dashboard / Analytics** — month view, aggregate counts, streak analytics (Redis-cached).
- **Workouts** — removed (tables dropped). See §7.

---

## 2. Solution layout (Clean Architecture)

```
Anitime.sln
├── src/
│   ├── Anitime.Domain          # Entities, enums. No dependencies.
│   ├── Anitime.Contracts       # Request/response DTOs (records). Shared by Api + Worker.
│   ├── Anitime.Application      # Abstractions (ports), Result pattern, business Rules, FluentValidation, CacheKeys.
│   ├── Anitime.Infrastructure   # EF Core (AppDbContext + configs + migrations), Auth (JWT/PBKDF2),
│   │                            # Messaging (RabbitMQ), Storage (MinIO), Caching (Redis), Options, DI.
│   ├── Anitime.Api             # ASP.NET Core controllers, mapping, Swagger, startup.
│   └── Anitime.Worker          # Background services: reminder dispatch + RabbitMQ consumer → push.
└── tests/
    └── Anitime.Application.Tests # xUnit: habit rules + validators.
```

**Dependency direction:** `Api`/`Worker` → `Infrastructure` → `Application` → (`Domain` + `Contracts`). Domain depends on nothing.

---

## 3. Key conventions & patterns (follow these when extending)

| Area | Convention |
|---|---|
| **Controllers** | `sealed`, inherit `BaseApiController`, `[Authorize]` + `[Route("api/...")]`. **Method-level `[FromServices]` injection** — no constructor DI in controllers. |
| **Errors** | `Result` / `Result<T>` pattern + `AppError`/`AppErrors`. `BaseApiController` translates to HTTP via `FromResult`/`FromCreatedResult`. Errors return `ApiErrorResponse(code, message, details, traceId)`. |
| **Validation** | FluentValidation; `BaseApiController.ValidateAsync(...)` runs validator and returns a 400 result, or `null` to continue. Validators auto-registered from `SignUpRequestValidator` assembly. |
| **DTOs** | `sealed record` in `Anitime.Contracts`. Enums serialized as strings (`JsonStringEnumConverter`). |
| **Entities** | Inherit `BaseEntity` (`Guid Id` + `IsDeleted`/`DeletedAtUtc` + `CreatedAtUtc`/`UpdatedAtUtc`). `sealed`. |
| **Soft delete** | Global query filter on every `BaseEntity` (auto-applied in `OnModelCreating`); `SaveChanges` rewrites `Delete` → `Modified` + sets `IsDeleted`. |
| **EF config** | One `IEntityTypeConfiguration<T>` per entity in `Persistence/Configurations`; **snake_case** table names; explicit `HasMaxLength` + indexes. Auto-applied via `ApplyConfigurationsFromAssembly`. |
| **Mapping** | Static extension methods in `ApiMappings` (`entity.ToResponse(...)`). No AutoMapper. |
| **Caching** | `IDistributedCache` (Redis); keys centralized in `Application/Caching/CacheKeys`. Cache-aside; invalidate on writes (e.g. dashboard cleared on habit/task change). |
| **Time** | `IClock` abstraction (`SystemClock`); client-local "today" resolved from request header via `ClientDateResolver`. |
| **Async** | All I/O async with `CancellationToken` threaded through. |
| **Options** | `IOptions<T>` bound from config sections (`JwtOptions`, `MinioOptions`, `RabbitMqOptions`). |

---

## 4. Data & persistence

- **PostgreSQL** via `Npgsql.EntityFrameworkCore.PostgreSQL`. Tables snake_case; collections stored as native `text[]`.
- **Migrations** are real (folder `Persistence/Migrations`). **Startup applies them** (`MigrateAsync`) under a PostgreSQL **advisory lock** (`Program.cs` `EnsureDatabaseReadyAsync`) so API + Worker can start concurrently without racing the schema.
  - ⚠️ The README still mentions `EnsureCreated()`; the actual code uses `Migrate()`. Add migrations with `dotnet ef migrations add <Name> -p src/Anitime.Infrastructure -s src/Anitime.Api`.
- **Worker** waits until there are **no pending migrations** before consuming (defers to API to migrate).
- `AppDbContextFactory` enables design-time tooling.

---

## 5. Cross-cutting infrastructure

| Concern | Implementation | Port (Application abstraction) |
|---|---|---|
| Auth tokens | `JwtTokenService` (HS256) | `IJwtTokenService` |
| Password hashing | `Pbkdf2PasswordHasher` | `IPasswordHasher` |
| Current user | `HttpUserContext` (reads `sub` claim) | `IUserContext` |
| Reminder events | `RabbitMqReminderPublisher` | `IReminderPublisher` |
| Push delivery | `LoggingPushNotificationSender` *(placeholder — swap for FCM/APNS)* | `IPushNotificationSender` |
| File storage | `MinioFileStorageService` | `IFileStorageService` |
| Cache | Redis (`AddStackExchangeRedisCache`) | `IDistributedCache` |
| Clock | `SystemClock` | `IClock` |

**Service lifetimes:** stateless infra services are `Singleton`; `IUserContext` is `Scoped`; `AppDbContext` is `Scoped`.

---

## 6. Reminder → notification flow

1. `ReminderDispatchWorker` (Worker) polls every 15s for due, enabled reminders (`ScheduledForUtc <= now`, take 100).
2. Publishes `ReminderTriggeredIntegrationEvent` to RabbitMQ; advances `ScheduledForUtc` (if `RepeatDaily`) or disables.
3. `ReminderNotificationConsumer` consumes (manual ack, QoS 10, requeue on failure) → `IPushNotificationSender`.
> Improvement noted in README: add an **outbox** around publishing for stronger delivery guarantees.

---

## 7. Workouts (removed)

The exercise library (`/api/exercises*`) and workout collections (`/api/workouts*`) were removed from the product, with their code, seed data and options. With the owner's approval (2026-11-15), migration `DropWorkoutTables` dropped the `exercises` and `workouts` tables and their data. To bring the feature back, restore the code from git history (before commit c0aadb3) and add a new migration.

---

## 9. Running locally

```bash
docker compose up --build                       # full stack
# or infra only:
docker compose up -d postgres rabbitmq redis minio
dotnet run --project src/Anitime.Api            # http://localhost:8080/swagger
dotnet run --project src/Anitime.Worker
```

- Migrations apply automatically on startup.
- **Secrets** live in a git-ignored `.env` (copy `.env.example`), which `docker compose` requires and `deploy-macos.sh` uploads. Postgres, Redis, RabbitMQ and the MinIO console bind to `127.0.0.1`; use an SSH tunnel to reach them on a server.

---

## 10. Build / test

```bash
dotnet build Anitime.sln           # clean
dotnet test                        # Application.Tests (rules + validators) and Api.Tests (real API + PostgreSQL, requires Docker)
```

**Notes concurrency:** `Note.VersionToken` is an EF concurrency token, so overlapping saves with one token resolve to exactly one `200` and the rest `409`. `PUT /api/notes/{id}` keeps stored content when both `contentJson` and `plainTextPreview` are omitted (installed apps send this). Limits: 5000 top-level blocks per note, JSON depth 64 (`400` beyond either), and `plainTextPreview` is shortened to 50,000 characters rather than rejected. Block nodes carry an optional `checked`.

**Resilience and limits:** the cache is wrapped by `ResilientDistributedCache`, so a Redis outage reads as a miss (each call can still wait about 5s). Signup, login, forgot-password and reset-password are limited to 20 requests per minute per IP (`RateLimiting:AuthPermitLimit`, keyed on `CF-Connecting-IP`). A refresh token can be reused for 30s after rotation. Reset codes: 15 minutes, 5 attempts (taken atomically before each check), a new code invalidates older ones; the legacy long token still works when no email is sent. Image uploads: over 5 MB returns 413 JSON; the request cap is 25 MB (below Kestrel's 30 MB default) so the body is read and the client gets the JSON. The worker skips reminder events more than 1 hour late.

**Performance:** `PerformanceApiTests` counts SQL commands per request (`QueryCounter`, an EF interceptor) and fails if a hot endpoint exceeds its budget or its query count grows with data (N+1). Dashboard and analytics load only habit windows and completed dates (`HabitHistoryQuery`) and aggregate tasks in SQL; calendar groups in SQL; habit and task lists project only response columns; a habit check loads only the target day's entry. `GET` lists marked `[ConditionalGet]` (notes, tasks, habits, reminders) return a body-hash `ETag` with `Cache-Control: private, no-cache`, and `304` on `If-None-Match`. Responses are Brotli/Gzip compressed except under `/api/auth` (BREACH). `DbContext` is pooled. Additive list options: `GET /api/tasks?from=&to=` and `GET /api/notes?view=summary` (no `contentJson`). The dashboard cache entry is tagged with the client day, so local midnight never serves yesterday. Benchmark: `ANITIME_PERF=1 ANITIME_PERF_OUT=perf.md dotnet test --filter Benchmark_HotEndpoints_P50`.

**Logging:** `Microsoft.AspNetCore.Hosting.Diagnostics` at `Information` logs method, path, status and duration for each request, with no bodies or auth headers.

> Tests are **not** run automatically. Add tests only when requested.

**Notes v2 (branch `notes-v2`, plan: app repo `docs/NOTES-PARITY.md`):**
- Note request bodies reject unknown JSON members with `400 invalid_request` (a resolver modifier in `Program.cs` over `NoteContractTypes.Strict`; `[InvalidRequestBody]` on `NotesController` shapes the error). Stored notes are still read leniently.
- `contentJson.version` 1 needs `blocks` (unchanged rules); version 2 needs `content`. `RichNoteV2Schema` enforces node types, parent/child rules, attrs, marks, link schemes, colours and limits (5,000 top-level, 50,000 nodes, 20,000 chars per text, 8 list levels, 50x1,000 tables with equal row widths, 100 images, 2 MB serialized → `413`). Version 2 previews are built by the server.
- A version 1 write over a stored version 2 note returns `409 note_format_too_new`. The first version 2 write over a version 1 note copies the old content into `note_content_backups` in the same save.
- `GET /api/app/config` (anonymous) returns `{minIosBuild, minAppVersion}` from `ClientApp:*` (env `MIN_IOS_BUILD`, `MIN_APP_VERSION`; defaults 0 / 0.0.0).
- Not yet: image `assetId` ownership (no attachments until P2), backup retention (plan says 90 days; nothing deletes them yet).

**Notes P2/P3 (branch `notes-v2-p2`, migration `AddNotesOrganisation`):**
- Folders (`NoteFoldersController`, `note_folders`): two levels; deleting a folder unfiles its notes (or `?deleteNotes=true` sends them to Recently Deleted) and promotes subfolders. `POST /api/notes/move` files notes without rotating version tokens.
- Note columns derived on every content save by `Application/Rules/NoteContentIndex.cs` (via `Api/Infrastructure/NoteContentWriter.cs`): `Tags` (typed #tags from v2 text + `ExplicitTags`), `LinkedNoteIds` (`anitime-note:` links → backlinks), `AssetIds` (image nodes → ownership check and orphan cleanup), `HasChecklist`, `HasImage`. Array columns have GIN indexes; filters are written so Npgsql emits `<@` / `&&` (`NoteQueries`).
- Metadata: `FolderId`, `IsLocked` (UI lock only; content still syncs; search matches locked notes by title only), `PaperStyle` (plain/lines/grid). Note colours accept Apple's palette names. `PUT` keeps fields it doesn't receive (installed apps).
- Smart folders (`smart_folders`, filter JSON) are evaluated by the server: `GET /api/notes?smartFolderId=`.
- Recently Deleted = the existing soft delete. `/api/notes/deleted`, `/deleted/{id}`, `/{id}/restore`, `/{id}/forever`; `/{id}/conflict-copy` saves the unchosen side of a 409 straight into it. The worker (`NotesMaintenanceWorker`, hourly) purges after 30 days and deletes images unreferenced for 7 days (`Infrastructure/Notes/NotesMaintenance.cs`).
- Attachments (`note_attachments`, MinIO `users/{id}/notes/`): upload, then a 10-minute HMAC-signed URL (`AttachmentUrlSigner`, key derived from the JWT key with HKDF).
- Search: `GET /api/notes/search?q=`. No database extension (the server can't install any; `DatabaseExtensionTests` guards this). `notes.SearchText` = lowercased title + preview (title only when locked), set in `AppDbContext` on every save via `Note.BuildSearchText`. Each word is matched with `strpos` (C# `IndexOf`; `Contains` would become a slower `LIKE`), through the `(UserId, IsDeleted, UpdatedAtUtc)` index, so only the user's live notes are read, newest first, stopping at the limit. The column uses lz4 compression where available (several times faster to decompress than pglz). Heavy account (300 × 13 KB + 3,000 short notes): p50 about 25–35 ms (`NoteSearchPerformanceTests`). Substring rather than full-text search, because Postgres has no Georgian dictionary and as-you-type search needs partial words.
