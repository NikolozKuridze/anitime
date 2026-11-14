# Working Notes

## Done (2026-11-07/08): notes investigation and full backend audit
See git history for the full list. Tests: `dotnet test` (Docker required). The worker, RabbitMQ and MinIO paths have no automated tests; they were checked by hand on a local stack.

## Done (2026-11-14): performance pass
Query-count budgets, timing benchmark, SQL aggregates for dashboard/analytics/calendar, list projections, ETag + compression, pooled DbContext, dashboard day-boundary cache fix, additive `tasks?from&to` and `notes?view=summary`, `scripts/seed-local.sh`. See PROJECT.md §10.

## Next (not started)
- Cycle/period tracking (waits for the redesign). Fits as a per-user, per-day entity: `CycleDay` (`UserId`, `Date`, flow, symptoms, note) with a unique `(UserId, Date)` index, a `GET /api/cycle?from=&to=` range endpoint marked `[ConditionalGet]`, and optional per-day counts merged into `CalendarController`.

## Needs server access
- Deploy `deploy-macos.sh`: it replaces the remote app folder (everything except `.git`).
- Rotate the secrets in `.env`. They sat in plain text in `docker-compose.yml` with Postgres, Redis and RabbitMQ ports public. A Postgres password change also needs `ALTER USER` on the server, because the env var only applies to a new volume. Rotating the JWT key invalidates current access tokens, so apps will refresh.
- Confirm nothing external uses the ports now bound to `127.0.0.1` (55432, 5673, 15673, 6380, 19101).
- Check whether port 1717 can be limited to Cloudflare: direct access lets a client spoof `CF-Connecting-IP` and bypass the auth rate limit. Port 19100 stays public because `cdn.anitime.space` may depend on it.

## Needs a product decision or provider
- Password reset emails need a Resend account: verify the sending domain, then set `EMAIL_PROVIDER=Resend`, `RESEND_API_KEY` and `EMAIL_FROM` in the server `.env`.
- Push stays `LoggingPushNotificationSender` on purpose: the app shows reminders as local notifications.
- Swagger is public in production.

## Known limits
- Full `GET /api/tasks` and `GET /api/notes` stay heavy for big accounts (serializing MBs of JSON); the app should move to `?from=&to=` and `?view=summary`.
- `RabbitMqReminderPublisher` opens a channel per message (about 4 broker round trips); negligible at the worker's 15 s / 100-reminder pace, so left as is.
- A Redis outage adds about 5s per cache call until Redis returns; a failed invalidation can leave a dashboard stale for up to 5 minutes.
- Duplicate reminder suppression uses the cache, so it can't suppress during a Redis outage.
- Dashboard and analytics count task completions by UTC date, not the client's local date.

## Key decisions
- Reset codes are hashed with their row id (equal codes for two users never collide on the unique index). Installed app versions that send a code without `email` can't use it; they need the new app build.
- Reset emails are sent from a background queue so response time doesn't reveal whether an account exists. Mail queued at shutdown is lost; the user requests a new code.
- A child node with neither `textNode` nor `blockNode` is still accepted; rejecting it could break stored notes.
