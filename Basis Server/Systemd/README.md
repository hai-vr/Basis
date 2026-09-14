# Running the Basis server with systemd

`basis-server.service` runs the server as a proper daemon:

- systemd waits until the server is actually listening before it reports the unit as started, and is told when it begins shutting down.
- Failures come back as exit codes, so `Restart=on-failure` restarts a crashed server but leaves a broken configuration alone.
- Logs go to the journal with their severity, so `journalctl -p warning` works.
- `basisctl` gives you the server console while it runs in the background.
- `systemctl stop` (SIGTERM) shuts the server down gracefully: players are disconnected cleanly, pending permission changes are saved and the log files are flushed.

## Install

The paths below match the unit file. Change both together if you install somewhere else.

```bash
sudo useradd --system --home-dir /opt/basis-server --shell /usr/sbin/nologin basis
sudo mkdir -p /opt/basis-server
sudo cp -r ./* /opt/basis-server/            # the contents of a Basis Server release
sudo chown -R basis:basis /opt/basis-server
sudo chmod +x /opt/basis-server/BasisNetworkConsole /opt/basis-server/basisctl
sudo ln -s /opt/basis-server/basisctl /usr/local/bin/basisctl

sudo cp basis-server.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now basis-server
```

If you run the framework-dependent build instead of a self-contained release, point `ExecStart` at the runtime:
`ExecStart=/usr/bin/dotnet /opt/basis-server/BasisNetworkConsole.dll`. Either way `ExecStart` must start the server directly, not through a shell script that stays running, or systemd will ignore its readiness and watchdog messages.

To let other accounts use `basisctl`, add them to the `basis` group (`sudo usermod -aG basis <user>`, then log in again).

## First boot

A service has no terminal, so the setup wizard cannot ask for an admin. Either:

- start the service and add one with `basisctl perm user group add did:key:z6Mk... admin`, or
- set it before the first start with `sudo systemctl edit basis-server`:

  ```ini
  [Service]
  Environment=BasisFirstAdmin=did:key:z6Mk...
  ```

Any other `config.xml` setting can be overridden the same way, for example `Environment=PeerLimit=500`.

First-boot tuning is skipped under systemd unless `BASIS_AUTOTUNE` is set to `quick`, `medium` or `long`. While it runs the server keeps extending its start timeout, so the unit does not time out.

## Exit codes

| Code | Meaning | What systemd does with the shipped unit |
| ---- | ------- | --------------------------------------- |
| 0 | Clean shutdown (`systemctl stop`, `/shutdown`, Ctrl+C) | Stays stopped |
| 69 | Could not start listening, for example the UDP port or the REST API port is already in use | Restarts after 5 seconds |
| 70 | Crashed on an unhandled exception (logged in full) | Restarts after 5 seconds |
| 75 | `/restart` was requested | Starts it again straight away (`RestartForceExitStatus=75`) |
| 78 | `config.xml` could not be read | Stays stopped until you fix it (`RestartPreventExitStatus=78`) |

systemd shows the names too, for example `status=78/CONFIG`. After five failed starts in ten minutes it stops trying; `systemctl reset-failed basis-server` clears that.

The server also sends a watchdog ping every 30 seconds (`WatchdogSec=60`). If the process stops scheduling work for a minute, systemd kills and restarts it. Remove `WatchdogSec` to turn that off.

## Logs

```bash
journalctl -u basis-server -f              # follow
journalctl -u basis-server -p warning      # warnings and errors only
journalctl -u basis-server --since today
```

When the server's output is connected to the journal it writes plain lines with a severity prefix instead of timestamps and colours. The daily files under `logs/` are still written unless `HasFileSupport` is false.

`BASIS_LOG_TARGET` chooses where log lines go:

| Value | Effect |
| ----- | ------ |
| `auto` (default) | Journal format when stdout is the journal, the normal console otherwise |
| `journal` | Always use the journal format |
| `syslog` | Normal console output, and every line is also sent to `/dev/log`. Use this when the server is not started by systemd (for example inside `screen`) but should still reach `journalctl -t basis-server` or rsyslog |
| `console` | Never change the console output |

## basisctl

`basisctl` talks to the server over a local Unix socket. Only the server's user and group can open it.

```bash
basisctl                          # attach: live log plus a prompt, type exit or Ctrl+C to detach
basisctl status
basisctl players
basisctl config PeerLimit 500
basisctl perm user group add did:key:z6Mk... admin
basisctl restart
basisctl shutdown
basisctl help                     # every console command
```

The leading `/` of a command is optional. Commands run through the same command table as the interactive console, one at a time, and their output also lands in the server log. `basisctl` exits with 0 when the command ran, 1 when it failed, 2 for an unknown command, 3 when no server is reachable and 4 when the socket refused you, so it can be used from scripts.

Under systemd the socket is `/run/basis-server/basis.sock`. Started by hand, the server puts it beside its executable as `basis.sock`, which is where `basisctl` looks first. Set `BASIS_CONTROL_SOCKET` (or pass `basisctl --socket <path>`) to use another path, or set `BASIS_CONTROL_SOCKET=off` to run the server without one.

## Stopping and restarting

SIGTERM and SIGINT start a graceful shutdown. A second signal ends the process immediately. `TimeoutStopSec=30` is the upper bound systemd waits before it kills the server.

`/restart` (from `basisctl` or the console) exits with code 75 under systemd, and systemd starts a fresh process. Outside systemd the server still launches its own replacement.
