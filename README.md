# Anitime Backend

Lean modular monolith for habits, daily tracking, tasks, dashboard analytics, reminders, and mobile-ready notification flow.

## Stack

- .NET 9 minimal API
- PostgreSQL with EF Core
- JWT authentication
- RabbitMQ for reminder events
- Redis for cached profile/dashboard reads
- MinIO for profile/background image storage
- Worker service for reminder dispatch and notification consumption
- Docker Compose for local infrastructure

## Features

- Signup, login, forgot password, reset password
- User profile data with gender and date of birth
- Profile settings for transparent/solid/gradient/image backgrounds
- Profile image and background image upload
- 30-day habit challenges with daily check tracking
- Task list with checklist items
- Dashboard counts
- Calendar month view
- Analytics and streak tracking
- Reminder scheduling and queue-based push notification pipeline
- Workout exercise library (873 public-domain exercises with instructions, muscles, and images) — `/api/exercises`
- Curated workout collections (Leg Day, Push, Core, HIIT, etc.) with embeddable YouTube videos and related exercises — `/api/workouts`

## Run with Docker

```bash
cp .env.example .env   # then fill in the secrets (JWT_SIGNING_KEY: 32+ characters)
docker compose up -d --build
scripts/seed-local.sh  # demo login: demo@anitime.local / Demo-Password-123 (localhost only)
```

- API: `http://localhost:1717` (Swagger UI: `http://localhost:1717/swagger`)
- RabbitMQ UI: `http://localhost:15673` (bound to 127.0.0.1)
- MinIO API: `http://localhost:19100`
- Stop: `docker compose down` (add `-v` to also delete the local data)

## Run locally

```bash
docker compose up -d postgres rabbitmq
dotnet build
dotnet run --project src/Anitime.Api
dotnet run --project src/Anitime.Worker
```

## Notes

- Password reset emails a 6-digit code through Resend (`EMAIL_PROVIDER=Resend`, `RESEND_API_KEY`, `EMAIL_FROM` in `.env`). The code lasts 15 minutes and allows 5 attempts. The app sends `{ email, token: code, newPassword }` to `/api/auth/reset-password`.
- Without those settings, production sends nothing and logs a warning without the code. In Development the email is written to the log and the code is returned as `debugToken`.
- The notification sender is currently a logging placeholder. Replace `LoggingPushNotificationSender` with Firebase/APNS integration for production.
- For a higher delivery guarantee later, add an outbox table around reminder event publishing.
- The sample uses `EnsureCreated()` for frictionless startup. If you want formal migrations, switch startup to `Migrate()` and add an initial migration with `dotnet ef`.
- EF Core migrations run automatically when the API starts.
