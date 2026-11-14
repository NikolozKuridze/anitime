#!/usr/bin/env bash
# Creates (or reuses) a demo account with sample habits, tasks, notes and reminders on a LOCAL API.
# Usage: scripts/seed-local.sh [base-url]   (default http://localhost:1717)
# Refuses to run against anything but localhost, so it can never touch production.
set -euo pipefail

BASE_URL="${1:-http://localhost:1717}"
EMAIL="demo@anitime.local"
PASSWORD="Demo-Password-123"

case "$BASE_URL" in
  http://localhost:*|http://127.0.0.1:*) ;;
  *) echo "Refusing to seed $BASE_URL: only localhost is allowed." >&2; exit 1 ;;
esac

json() { python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }
post() { curl -fsS -X POST "$BASE_URL$1" -H "Content-Type: application/json" -H "Authorization: Bearer ${TOKEN:-}" -d "$2"; }

signup=$(curl -sS -o /tmp/anitime-seed.$$ -w '%{http_code}' -X POST "$BASE_URL/api/auth/signup" -H "Content-Type: application/json" \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"name\":\"Demo\",\"gender\":\"Female\"}")
if [ "$signup" = "409" ]; then
  echo "Demo account exists; logging in (no new sample data)."
  curl -fsS -X POST "$BASE_URL/api/auth/login" -H "Content-Type: application/json" \
    -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" >/dev/null
  rm -f /tmp/anitime-seed.$$
  echo "Login: $EMAIL / $PASSWORD"
  exit 0
fi
[ "$signup" = "200" ] || { echo "Signup failed with HTTP $signup" >&2; cat /tmp/anitime-seed.$$ >&2; exit 1; }
TOKEN=$(json "['accessToken']" < /tmp/anitime-seed.$$)
rm -f /tmp/anitime-seed.$$

today=$(date -u +%Y-%m-%d)
days_ago() { python3 -c "import datetime; print((datetime.date.today()-datetime.timedelta(days=$1)).isoformat())"; }

for habit in "Drink water|💧" "Read 20 pages|📚" "Stretch|🧘"; do
  id=$(post /api/habits "{\"title\":\"${habit%%|*}\",\"emoji\":\"${habit##*|}\",\"durationDays\":30,\"startDate\":\"$(days_ago 6)\"}" | json "['id']")
  for d in 6 5 4 2 1 0; do post "/api/habits/$id/check" "{\"date\":\"$(days_ago $d)\"}" >/dev/null; done
done

for d in 0 0 1 2 3; do
  id=$(post /api/tasks "{\"title\":\"Sample task $d\",\"date\":\"$(days_ago $d)\",\"checklistItems\":[{\"title\":\"Step one\"},{\"title\":\"Step two\"}]}" | json "['id']")
  [ "$d" -gt 0 ] && post "/api/tasks/$id/toggle" '{"isCompleted":true}' >/dev/null
done

for n in "Shopping list|Milk, eggs, bread" "Ideas|Try the new workout plan"; do
  post /api/notes "{\"title\":\"${n%%|*}\",\"contentJson\":{\"version\":1,\"blocks\":[{\"type\":\"paragraph\",\"align\":\"left\",\"children\":[{\"textNode\":{\"type\":\"text\",\"text\":\"${n##*|}\"}}]}]},\"plainTextPreview\":\"${n##*|}\",\"fontFamily\":\"system-ui\",\"fontSize\":16,\"fontWeight\":400,\"textColor\":\"#111827\",\"backgroundColor\":\"#FFFFFF\",\"isPinned\":false,\"isArchived\":false}" >/dev/null
done

post /api/reminders "{\"title\":\"Evening walk\",\"message\":\"Time for a walk\",\"scheduledForUtc\":\"${today}T18:00:00Z\",\"repeatDaily\":true}" >/dev/null

echo "Seeded. Login: $EMAIL / $PASSWORD"
