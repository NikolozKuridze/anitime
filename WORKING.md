# Working Notes

## Done (2026-11-07/08): notes save investigation
- Fixed: lost updates on overlapping note saves, metadata-only `PUT` returning 400, migrated notes reading back empty, NUL characters and nesting (up to the 64-level JSON limit) causing 500s, optional `fontFamily`, signup and first-habit-check races, empty 500 bodies, invisible request statuses.
- Tests: `tests/Anitime.Api.Tests` (needs Docker) and `NoteValidatorTests`.

## Open
- `docker-compose.yml` holds live secrets and must stay out of git until they are rotated and moved to a secret store. Infra ports are published in the same file.
- `AuthController` logs raw password reset tokens at Information, and that log is the only delivery path (no email is sent).
- No rate limiting on auth endpoints.
- Refresh token rotation has no reuse grace period, and parallel refreshes are not atomic.
- Redis outage after a committed write returns 500 (cache invalidation is not fault tolerant).
- Task `clientRequestId` reused after soft delete returns 500.
- Reminder dispatch can double-send if the worker fails between publish and save (no outbox).
- Image uploads use `DisableRequestSizeLimit`: the 5 MB check runs only after the whole body is received, and the content type is client-claimed.

## Key decisions
- A child node with neither `textNode` nor `blockNode` is still accepted; rejecting it could break stored notes.
