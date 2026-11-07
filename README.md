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
docker compose up --build
```

API:

- Swagger UI: `http://localhost:8080/swagger`
- RabbitMQ UI: `http://localhost:15672`
- MinIO API: `http://localhost:9000`
- MinIO Console: `http://localhost:9001`

## Run locally

```bash
docker compose up -d postgres rabbitmq
dotnet build
dotnet run --project src/Anitime.Api
dotnet run --project src/Anitime.Worker
```

## Notes

- Password reset returns a `debugToken` only in development.
- The notification sender is currently a logging placeholder. Replace `LoggingPushNotificationSender` with Firebase/APNS integration for production.
- For a higher delivery guarantee later, add an outbox table around reminder event publishing.
- The sample uses `EnsureCreated()` for frictionless startup. If you want formal migrations, switch startup to `Migrate()` and add an initial migration with `dotnet ef`.
- Because the sample uses `EnsureCreated()`, schema changes require recreating the PostgreSQL volume during development: `docker compose down -v`.
