#!/usr/bin/env bash

set -euo pipefail

DEFAULT_REMOTE_BASE="/home"
DEFAULT_APP_NAME="anitime"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing required command: $1" >&2
    exit 1
  fi
}

prompt_with_default() {
  local label="$1"
  local default_value="$2"
  local result

  read -r -p "$label [$default_value]: " result
  if [[ -z "$result" ]]; then
    result="$default_value"
  fi

  printf '%s' "$result"
}

require_command ssh
require_command scp
require_command tar

if [[ ! -f "$SCRIPT_DIR/.env" ]]; then
  echo "Missing $SCRIPT_DIR/.env. Copy .env.example and fill in the secrets." >&2
  exit 1
fi

SERVER_IP="$(prompt_with_default "Server IP" "")"
if [[ -z "$SERVER_IP" ]]; then
  echo "Server IP is required." >&2
  exit 1
fi

SSH_USER="$(prompt_with_default "SSH user" "ubuntu")"
AUTH_METHOD="$(prompt_with_default "Auth method (password/key)" "password")"
SSH_KEY_PATH=""
SSHPASS_PREFIX=()
SSH_OPTIONS=(-o StrictHostKeyChecking=accept-new)
SCP_OPTIONS=(-o StrictHostKeyChecking=accept-new)

case "$AUTH_METHOD" in
  password|Password|PASSWORD)
    require_command sshpass
    read -r -s -p "SSH password: " SSH_PASSWORD
    echo
    if [[ -z "$SSH_PASSWORD" ]]; then
      echo "SSH password is required." >&2
      exit 1
    fi
    SSHPASS_PREFIX=(sshpass -p "$SSH_PASSWORD")
    ;;
  key|Key|KEY)
    SSH_KEY_PATH="$(prompt_with_default "SSH key path" "$HOME/.ssh/id_rsa")"
    if [[ ! -f "$SSH_KEY_PATH" ]]; then
      echo "SSH key not found: $SSH_KEY_PATH" >&2
      exit 1
    fi
    SSH_OPTIONS+=(-i "$SSH_KEY_PATH" -o BatchMode=yes)
    SCP_OPTIONS+=(-i "$SSH_KEY_PATH" -o BatchMode=yes)
    ;;
  *)
    echo "Unsupported auth method: $AUTH_METHOD" >&2
    exit 1
    ;;
esac

DEFAULT_REMOTE_PATH="${DEFAULT_REMOTE_BASE}/${SSH_USER}/${DEFAULT_APP_NAME}"
REMOTE_PATH="$(prompt_with_default "Remote deploy path" "$DEFAULT_REMOTE_PATH")"

ARCHIVE_NAME="${DEFAULT_APP_NAME}-deploy.tar.gz"
ARCHIVE_PATH="/tmp/${ARCHIVE_NAME}"

echo
echo "Local project: $SCRIPT_DIR"
echo "Remote host: ${SSH_USER}@${SERVER_IP}"
echo "Remote path: $REMOTE_PATH"
echo

read -r -p "Continue with deployment? [Y/n]: " confirm
if [[ "${confirm:-Y}" =~ ^[Nn]$ ]]; then
  echo "Deployment cancelled."
  exit 0
fi

echo "Creating deployment archive..."
tar \
  --exclude=".git" \
  --exclude="**/bin" \
  --exclude="**/obj" \
  --exclude=".DS_Store" \
  --exclude="._*" \
  --exclude="**/._*" \
  --exclude="__MACOSX" \
  -czf "$ARCHIVE_PATH" \
  -C "$SCRIPT_DIR" .

cleanup() {
  rm -f "$ARCHIVE_PATH"
}
trap cleanup EXIT

echo "Preparing remote directory..."
("${SSHPASS_PREFIX[@]}" ssh "${SSH_OPTIONS[@]}" "${SSH_USER}@${SERVER_IP}" \
  "mkdir -p '$REMOTE_PATH'"
)

echo "Uploading archive..."
("${SSHPASS_PREFIX[@]}" scp "${SCP_OPTIONS[@]}" "$ARCHIVE_PATH" \
  "${SSH_USER}@${SERVER_IP}:/tmp/${ARCHIVE_NAME}"
)

echo "Extracting project on server..."
("${SSHPASS_PREFIX[@]}" ssh "${SSH_OPTIONS[@]}" "${SSH_USER}@${SERVER_IP}" <<EOF
set -euo pipefail
mkdir -p "$REMOTE_PATH"
find "$REMOTE_PATH" -mindepth 1 -maxdepth 1 ! -name '.git' -exec rm -rf {} +
tar -xzf "/tmp/${ARCHIVE_NAME}" -C "$REMOTE_PATH"
find "$REMOTE_PATH" -name '._*' -delete
find "$REMOTE_PATH" -name '.DS_Store' -delete
find "$REMOTE_PATH" -name '__MACOSX' -type d -prune -exec rm -rf {} +
rm -f "/tmp/${ARCHIVE_NAME}"
cd "$REMOTE_PATH"
docker compose down || true
docker compose up -d --build
docker compose ps
EOF
)

echo
echo "Deployment completed."
echo "API should be reachable at: http://${SERVER_IP}:1717/swagger"
