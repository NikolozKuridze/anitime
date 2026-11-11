# Anitime — Architecture & Project Summary

> Lean **modular monolith** backend for a habit / productivity + (now) workout app.
> .NET 9, Clean Architecture, PostgreSQL, JWT, RabbitMQ, Redis, MinIO, Docker Compose.

---

## 1. What the system does

A mobile-first backend (JWT, CORS for Capacitor/Ionic clients) covering:

- **Auth** — signup, login, refresh tokens, forgot/reset password by emailed 6-digit code (PBKDF2 hashing). Email goes through `IEmailOutbox` (background queue) to `IEmailSender` (`Infrastructure/Email`: Resend, a Development log sender, or a no-op that warns).
- **Profile** — name, gender, DOB, age; profile + background images (MinIO); background style settings (transparent/solid/gradient/image).
- **Habits** — 30-day habit challenges with daily check toggling, current/best streaks, progress.
- **Tasks** — task list with checklist items, completion tracking.
- **Notes** — rich-text notes with formatting (align, format, plain-text preview).
- **Reminders** — scheduled reminders → RabbitMQ event → push notification pipeline.
- **Calendar / Dashboard / Analytics** — month view, aggregate counts, streak analytics (Redis-cached).
- **Exercise Library** *(new)* — public-domain catalog of 873 exercises with instructions, muscle groups, and demonstration images. See §7.

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
│   ├── Anitime.Api             # ASP.NET Core controllers, mapping, Swagger, startup, exercise seeder.
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
| **Options** | `IOptions<T>` bound from config sections (`JwtOptions`, `MinioOptions`, `RabbitMqOptions`, `ExerciseLibraryOptions`). |

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

## 7. Exercise Library feature (added on branch `feature/exercise-library`)

**Source:** [Free Exercise DB](https://github.com/yuhonas/free-exercise-db) — **public domain (Unlicense)**, no API key, no attribution required. 873 exercises with name, instructions, primary/secondary muscles, equipment, level, category, and demonstration **images** (start/end JPGs). *(This dataset has no videos/GIFs — see §8 for video options.)*

**How it's wired (follows every convention above):**

- **Domain:** `Exercise : BaseEntity` (`Slug`, `Name`, `Category`, `Level`, `Force?`, `Mechanic?`, `Equipment?`, `PrimaryMuscles`, `SecondaryMuscles`, `Instructions`, `ImagePaths`).
- **EF:** `ExerciseConfiguration` → table `exercises`, `text[]` columns, unique `Slug`, indexes on category/equipment/level + **GIN index** on `PrimaryMuscles`. Migration `AddExercises`.
- **Seeding:** `ExerciseCatalogSeeder` reads the bundled snapshot `src/Anitime.Api/Data/exercises.json` (copied to output) and seeds the table **once** (idempotent — skips if non-empty) at startup, after migrations.
- **Images:** stored as relative paths; absolute URLs built at the API boundary from `ExerciseLibraryOptions.ImageBaseUrl` (config section `ExerciseLibrary`). Defaults to the upstream CDN; **point it at your own MinIO/CDN in production** to avoid hot-linking GitHub.
- **API (`/api/exercises`, `[Authorize]`):**
  - `GET /api/exercises` — paged list; filters: `search` (name ILIKE), `muscle` (primary or secondary), `equipment`, `category`, `level`; `page`/`pageSize` (default 20, max 100).
  - `GET /api/exercises/filters` — distinct muscles/equipment/categories/levels (Redis-cached 12h, key `exercises:filters`).
  - `GET /api/exercises/{slug}` — full detail (instructions + all image URLs).

**Legal:** public domain → safe for commercial use, no attribution. If you later add wger/ExerciseDB media (AGPL, per-item licenses), you must surface attribution.

---

## 8. Workout collections + video (implemented)

The exercise dataset is images-only, so motion is provided via **YouTube embedding** — the legal, keyless, full-coverage option (embedding is permitted by YouTube ToS; downloading/re-hosting is not, so we store only video ids).

**`Workout` entity** = a curated collection (e.g. "Leg Day", "Push Day", "HIIT Cardio") with: `Slug`, `Title`, `Description`, `Category` (Lower Body / Upper Body / Full Body / Core / Cardio / Mobility), `Level`, `DurationMinutes`, `YouTubeVideoId`, `TargetMuscles[]`, `DisplayOrder`.

- **Seed:** `Data/workouts.json` (11 curated collections) → `WorkoutCatalogSeeder` (idempotent, runs at startup). **Every YouTube id was verified embeddable** via the keyless oEmbed endpoint (`https://www.youtube.com/oembed?...`) at curation time — no dead links shipped. Re-validate periodically; replace ids in the JSON to update content (no code change).
- **Catalog tie-in:** a workout's `TargetMuscles` resolve **related exercises** from the 873-exercise catalog at read time (no rigid join table), so each collection shows a matching exercise list.
- **API (`/api/workouts`, `[Authorize]`):**
  - `GET /api/workouts` — list collections (optional `?category=`), ordered by `DisplayOrder`.
  - `GET /api/workouts/{slug}` — detail: full routine `videoEmbedUrl` + `thumbnailUrl` + related exercises.
- **URLs** built at the API boundary: embed `https://www.youtube.com/embed/{id}`, thumbnail `https://img.youtube.com/vi/{id}/hqdefault.jpg`.
- `Exercise` also gained an optional `YouTubeVideoId` (null today) → `videoEmbedUrl` in its detail response, so per-exercise demo clips can be filled in later without schema changes.

**Frontend:** render `videoEmbedUrl` in a YouTube iframe (web) or `react-native-youtube-iframe` (mobile).

> Want self-hosted/owned video instead of embeds? wger exposes ~78 real CC-BY-SA videos via its API (attribution required, sparse coverage); ExerciseDB GIFs require a paid commercial license — the free GIF mirrors redistribute licensed media and are **not** safe for a commercial app.

---

## 9. Running locally

```bash
docker compose up --build                       # full stack
# or infra only:
docker compose up -d postgres rabbitmq redis minio
dotnet run --project src/Anitime.Api            # http://localhost:8080/swagger
dotnet run --project src/Anitime.Worker
```

- Migrations apply automatically on startup; the exercise catalog seeds automatically on first run.
- **Secrets** live in a git-ignored `.env` (copy `.env.example`), which `docker compose` requires and `deploy-macos.sh` uploads. Postgres, Redis, RabbitMQ and the MinIO console bind to `127.0.0.1`; use an SSH tunnel to reach them on a server.

---

## 10. Build / test

```bash
dotnet build Anitime.sln           # clean
dotnet test                        # Application.Tests (rules + validators) and Api.Tests (real API + PostgreSQL, requires Docker)
```

**Notes concurrency:** `Note.VersionToken` is an EF concurrency token, so overlapping saves with one token resolve to exactly one `200` and the rest `409`. `PUT /api/notes/{id}` keeps stored content when both `contentJson` and `plainTextPreview` are omitted (installed apps send this). Limits: 5000 top-level blocks per note, JSON depth 64 (`400` beyond either), and `plainTextPreview` is shortened to 50,000 characters rather than rejected. Block nodes carry an optional `checked`.

**Resilience and limits:** the cache is wrapped by `ResilientDistributedCache`, so a Redis outage reads as a miss (each call can still wait about 5s). Signup, login, forgot-password and reset-password are limited to 20 requests per minute per IP (`RateLimiting:AuthPermitLimit`, keyed on `CF-Connecting-IP`). A refresh token can be reused for 30s after rotation. Reset codes: 15 minutes, 5 attempts (taken atomically before each check), a new code invalidates older ones; the legacy long token still works when no email is sent. Image uploads: over 5 MB returns 413 JSON; the request cap is 25 MB (below Kestrel's 30 MB default) so the body is read and the client gets the JSON. The worker skips reminder events more than 1 hour late.

**Logging:** `Microsoft.AspNetCore.Hosting.Diagnostics` at `Information` logs method, path, status and duration for each request, with no bodies or auth headers.

> Tests are **not** run automatically. Add tests only when requested.
