#!/usr/bin/env bash
set -Eeuo pipefail

# Ubuntu/Linux watchdog for a source-run SS14 server.
#
# It starts the server, checks GitHub for a newer commit, notifies the running
# server through RobustToolbox's watchdog /update endpoint, waits for the server
# to exit at the end of the round, pulls the update, and starts it again.

ROOT="${ROOT:-$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)}"
REMOTE_URL="${REMOTE_URL:-https://github.com/ShalnayaKlyaksa/eclipse-server.git}"
REMOTE="${REMOTE:-origin}"
BRANCH="${BRANCH:-}"
STATUS_URL="${STATUS_URL:-http://127.0.0.1:1212}"
CHECK_INTERVAL="${CHECK_INTERVAL:-300}"
NOTIFY_RETRY_INTERVAL="${NOTIFY_RETRY_INTERVAL:-30}"
POLL_INTERVAL="${POLL_INTERVAL:-2}"
RESTART_DELAY="${RESTART_DELAY:-5}"
ALLOW_DIRTY="${ALLOW_DIRTY:-0}"
WATCHDOG_TOKEN="${WATCHDOG_TOKEN:-$(od -An -N32 -tx1 /dev/urandom | tr -d ' \n')}"
SERVER_COMMAND="${SERVER_COMMAND:-dotnet run --project Content.Server --}"
POST_UPDATE_COMMAND="${POST_UPDATE_COMMAND:-}"

server_pid=""
pending_update_sha=""
update_announced=0

log() {
    printf '[autoupdate] %s\n' "$*"
}

die() {
    log "error: $*"
    exit 1
}

require_tool() {
    command -v "$1" >/dev/null 2>&1 || die "required tool is missing: $1"
}

current_branch() {
    git -C "$ROOT" branch --show-current
}

local_head() {
    git -C "$ROOT" rev-parse HEAD
}

remote_head() {
    git -C "$ROOT" ls-remote "$REMOTE_URL" "refs/heads/$BRANCH" | awk '{ print $1 }'
}

fetch_update() {
    log "git fetch --prune $REMOTE $BRANCH"
    git -C "$ROOT" fetch --prune "$REMOTE" "$BRANCH"
}

apply_update() {
    if [[ "$ALLOW_DIRTY" != "1" ]]; then
        local dirty
        dirty="$(git -C "$ROOT" status --porcelain --untracked-files=no)"
        if [[ -n "$dirty" ]]; then
            log "tracked local changes are present; refusing to update:"
            printf '%s\n' "$dirty"
            die "commit/stash them or run with ALLOW_DIRTY=1"
        fi
    fi

    fetch_update
    log "git pull --ff-only $REMOTE $BRANCH"
    git -C "$ROOT" pull --ff-only "$REMOTE" "$BRANCH"
    log "git submodule update --init --recursive"
    git -C "$ROOT" submodule update --init --recursive

    if [[ -n "$POST_UPDATE_COMMAND" ]]; then
        log "running post-update command: $POST_UPDATE_COMMAND"
        (
            cd "$ROOT"
            eval "$POST_UPDATE_COMMAND"
        )
    fi
}

start_server() {
    # SERVER_COMMAND is intentionally evaluated as a shell command string so
    # admins can pass quoted paths/arguments through the environment.
    log "starting server: $SERVER_COMMAND --cvar watchdog.token=<hidden>"
    (
        cd "$ROOT"
        eval "exec $SERVER_COMMAND --cvar \"watchdog.token=$WATCHDOG_TOKEN\""
    ) &
    server_pid="$!"
}

stop_server() {
    if [[ -n "${server_pid:-}" ]] && kill -0 "$server_pid" >/dev/null 2>&1; then
        log "stopping server..."
        kill "$server_pid" || true
        sleep 30 &
        local timer_pid="$!"

        while kill -0 "$server_pid" >/dev/null 2>&1; do
            if ! kill -0 "$timer_pid" >/dev/null 2>&1; then
                log "server did not stop gracefully, killing..."
                kill -9 "$server_pid" || true
                break
            fi
            sleep 1
        done

        kill "$timer_pid" >/dev/null 2>&1 || true
    fi
}

notify_server() {
    local target_sha="$1"
    local body
    body="$(printf '{"reason":"UpdateAvailable","message":"GitHub update available: %s"}' "$target_sha")"

    if curl \
        --silent \
        --show-error \
        --fail \
        --max-time 10 \
        --request POST \
        --header "WatchdogToken: $WATCHDOG_TOKEN" \
        --header "Content-Type: application/json" \
        --data "$body" \
        "${STATUS_URL%/}/update" >/dev/null; then
        log "server notified; it will restart at round end"
        update_announced=1
    else
        log "failed to notify server, will retry"
    fi
}

on_exit() {
    stop_server
}

trap on_exit INT TERM

require_tool git
require_tool dotnet
require_tool curl
require_tool awk
require_tool od

if [[ -z "$BRANCH" ]]; then
    BRANCH="$(current_branch)"
fi

[[ -n "$BRANCH" ]] || die "cannot determine current branch; set BRANCH=..."

log "repository: $ROOT"
log "tracking: $REMOTE_URL $BRANCH"
log "status API: $STATUS_URL"

while true; do
    start_server
    last_check=0
    last_notify=0

    while kill -0 "$server_pid" >/dev/null 2>&1; do
        now="$(date +%s)"

        if [[ -z "$pending_update_sha" ]] && (( now - last_check >= CHECK_INTERVAL )); then
            last_check="$now"
            remote_sha="$(remote_head)"
            [[ -n "$remote_sha" ]] || die "branch '$BRANCH' was not found at $REMOTE_URL"

            local_sha="$(local_head)"
            if [[ "$remote_sha" != "$local_sha" ]]; then
                pending_update_sha="$remote_sha"
                update_announced=0
                log "update found: ${local_sha:0:12} -> ${remote_sha:0:12}"
                fetch_update
            else
                log "no update, current HEAD ${local_sha:0:12}"
            fi
        fi

        if [[ -n "$pending_update_sha" ]] && [[ "$update_announced" != "1" ]] && (( now - last_notify >= NOTIFY_RETRY_INTERVAL )); then
            last_notify="$now"
            notify_server "$pending_update_sha"
        fi

        sleep "$POLL_INTERVAL"
    done

    if wait "$server_pid"; then
        exit_code=0
    else
        exit_code="$?"
    fi
    log "server exited with code $exit_code"
    server_pid=""

    if [[ -n "$pending_update_sha" ]]; then
        log "applying pending update..."
        apply_update
        pending_update_sha=""
        update_announced=0
    else
        log "restarting after ${RESTART_DELAY}s"
        sleep "$RESTART_DELAY"
    fi
done
