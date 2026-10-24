#!/usr/bin/env python3
"""
Small watchdog for a source-run SS14 server.

It starts the server, checks GitHub for a newer commit, notifies the running
server through RobustToolbox's watchdog /update endpoint, waits for the server
to exit at the end of the round, pulls the update, and starts it again.
"""

from __future__ import annotations

import argparse
import json
import os
import secrets
import shlex
import subprocess
import sys
import time
from pathlib import Path
from typing import Sequence
from urllib.error import URLError
from urllib.request import Request, urlopen


DEFAULT_REPO_URL = "https://github.com/ShalnayaKlyaksa/eclipse-server.git"
DEFAULT_SERVER_COMMAND = "dotnet run --project Content.Server --"
DEFAULT_STATUS_URL = "http://127.0.0.1:1212"


def main() -> int:
    args = parse_args()
    root = Path(args.root).resolve()

    token = args.watchdog_token or os.environ.get("ECLIPSE_WATCHDOG_TOKEN") or secrets.token_urlsafe(32)
    branch = args.branch or current_branch(root)
    server_command = build_server_command(args.server_command, token)

    print(f"[autoupdate] repository: {root}")
    print(f"[autoupdate] tracking: {args.remote_url} {branch}")
    print(f"[autoupdate] status API: {args.status_url}")

    pending_update_sha: str | None = None
    update_announced = False

    while True:
        server = start_server(server_command, root)

        try:
            last_check = 0.0
            last_notify = 0.0

            while server.poll() is None:
                now = time.monotonic()

                if pending_update_sha is None and now - last_check >= args.check_interval:
                    last_check = now
                    remote_sha = remote_head(args.remote_url, branch, root)
                    local_sha = git_output(root, "rev-parse", "HEAD")

                    if remote_sha != local_sha:
                        pending_update_sha = remote_sha
                        update_announced = False
                        print(f"[autoupdate] update found: {local_sha[:12]} -> {remote_sha[:12]}")
                        fetch_update(root, args.remote, branch)
                    else:
                        print(f"[autoupdate] no update, current HEAD {local_sha[:12]}")

                if pending_update_sha is not None and not update_announced and now - last_notify >= args.notify_retry_interval:
                    last_notify = now
                    update_announced = notify_server(args.status_url, token, pending_update_sha)

                time.sleep(args.poll_interval)
        except KeyboardInterrupt:
            print("[autoupdate] interrupted, stopping server...")
            stop_server(server)
            return 130

        exit_code = server.returncode
        print(f"[autoupdate] server exited with code {exit_code}")

        if pending_update_sha is not None:
            print("[autoupdate] applying pending update...")
            apply_update(root, args.remote, branch, args.allow_dirty)
            pending_update_sha = None
            update_announced = False
        else:
            print(f"[autoupdate] restarting after {args.restart_delay:g}s")
            time.sleep(args.restart_delay)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run the SS14 server and restart it after GitHub updates are available."
    )
    parser.add_argument("--root", default=Path(__file__).resolve().parents[1], help="Repository root.")
    parser.add_argument("--remote-url", default=DEFAULT_REPO_URL, help="GitHub repository URL to check.")
    parser.add_argument("--remote", default="origin", help="Local git remote to fetch/pull from.")
    parser.add_argument("--branch", help="Branch to track. Defaults to the current branch.")
    parser.add_argument("--status-url", default=DEFAULT_STATUS_URL, help="Server status API base URL.")
    parser.add_argument(
        "--watchdog-token",
        help="Token for watchdog /update. Defaults to ECLIPSE_WATCHDOG_TOKEN or a generated token.",
    )
    parser.add_argument(
        "--server-command",
        default=DEFAULT_SERVER_COMMAND,
        help=(
            "Command used to run the server. The script appends "
            "'--cvar watchdog.token=...' to this command."
        ),
    )
    parser.add_argument("--check-interval", type=float, default=300, help="Seconds between GitHub checks.")
    parser.add_argument("--notify-retry-interval", type=float, default=30, help="Seconds between /update retries.")
    parser.add_argument("--poll-interval", type=float, default=2, help="Seconds between process polls.")
    parser.add_argument("--restart-delay", type=float, default=5, help="Delay before restarting after non-update exits.")
    parser.add_argument(
        "--allow-dirty",
        action="store_true",
        help="Allow pulling with tracked local changes present. By default this aborts.",
    )
    return parser.parse_args()


def build_server_command(command: str, token: str) -> list[str]:
    parts = shlex.split(command)
    parts.extend(["--cvar", f"watchdog.token={token}"])
    return parts


def start_server(command: Sequence[str], root: Path) -> subprocess.Popen[bytes]:
    print("[autoupdate] starting server:")
    print("[autoupdate] " + " ".join(shlex.quote(part) for part in command))
    return subprocess.Popen(command, cwd=root)


def stop_server(server: subprocess.Popen[bytes]) -> None:
    if server.poll() is not None:
        return

    server.terminate()
    try:
        server.wait(timeout=30)
    except subprocess.TimeoutExpired:
        server.kill()
        server.wait()


def notify_server(status_url: str, token: str, target_sha: str) -> bool:
    url = status_url.rstrip("/") + "/update"
    body = json.dumps(
        {
            "reason": "UpdateAvailable",
            "message": f"GitHub update available: {target_sha}",
        }
    ).encode("utf-8")

    request = Request(
        url,
        data=body,
        method="POST",
        headers={
            "Content-Type": "application/json",
            "WatchdogToken": token,
        },
    )

    try:
        with urlopen(request, timeout=10) as response:
            response.read()
        print("[autoupdate] server notified; it will restart at round end")
        return True
    except URLError as exc:
        print(f"[autoupdate] failed to notify server, will retry: {exc}")
        return False


def apply_update(root: Path, remote: str, branch: str, allow_dirty: bool) -> None:
    if not allow_dirty:
        dirty = git_output(root, "status", "--porcelain", "--untracked-files=no")
        if dirty:
            print("[autoupdate] tracked local changes are present; refusing to update:")
            print(dirty)
            print("[autoupdate] commit/stash them or rerun with --allow-dirty.")
            sys.exit(1)

    fetch_update(root, remote, branch)
    run(root, "git", "pull", "--ff-only", remote, branch)
    run(root, "git", "submodule", "update", "--init", "--recursive")


def fetch_update(root: Path, remote: str, branch: str) -> None:
    run(root, "git", "fetch", "--prune", remote, branch)


def remote_head(remote_url: str, branch: str, root: Path) -> str:
    output = git_output(root, "ls-remote", remote_url, f"refs/heads/{branch}")
    if not output:
        raise RuntimeError(f"Branch {branch!r} was not found at {remote_url}")

    return output.split()[0]


def current_branch(root: Path) -> str:
    branch = git_output(root, "branch", "--show-current")
    if not branch:
        raise RuntimeError("Cannot determine current branch. Pass --branch explicitly.")

    return branch


def git_output(root: Path, *args: str) -> str:
    return subprocess.check_output(("git", *args), cwd=root, text=True, encoding="utf-8").strip()


def run(root: Path, *args: str) -> None:
    print("[autoupdate] " + " ".join(shlex.quote(arg) for arg in args))
    subprocess.run(args, cwd=root, check=True)


if __name__ == "__main__":
    raise SystemExit(main())
