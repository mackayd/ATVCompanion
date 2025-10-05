# ATVCompanion

A small Windows app + CLI that lets you **wake**, **pair**, and **put in standby** Philips Android TVs that expose the **JointSPACE v6** API. It’s built for everyday use (a simple window with three buttons) and structured for easy extension (plugins, CLI, service).

> ✅ **Current milestone:** UI pairs successfully, Wake-on-LAN works, Standby works, config is persisted and reused across runs, and the CLI reads the same config.
> ⚠️ **“Create Tasks”** (Windows Task Scheduler helper) exists but is still being finalized.

---

## Table of Contents

* [Features](#features)
* [Supported TVs](#supported-tvs)
* [Repository Layout](#repository-layout)
* [Requirements](#requirements)
* [Build](#build)
* [Run](#run)

  * [UI](#ui-recommended)
  * [CLI](#cli)
* [Configuration](#configuration)
* [Scheduled Tasks (WIP)](#scheduled-tasks-work-in-progress)
* [Troubleshooting](#troubleshooting)
* [Security & Privacy](#security--privacy)
* [Roadmap](#roadmap)
* [Contributing](#contributing)
* [License](#license)
* [Acknowledgements](#acknowledgements)

---

## Features

* **Philips JointSPACE v6 pairing** (secure HMAC + Digest flow: `/6/pair/request` → `/6/pair/grant`)
* **Wake-on-LAN** (broadcast IP inferred from TV IP; override if needed)
* **Standby** via JointSPACE (`/6/input/key` with Digest auth)
* **Shared configuration** between **UI** and **CLI**
  Saved at: `%ProgramData%\ATVCompanion\Config.json`
* **Inline logging in the UI** for quick diagnostics (no separate console window)
* Built on **.NET 8**, with clean layering for future extensions

---

## Supported TVs

* **Philips Android TV (TP-Vision) with JointSPACE v6** enabled
  Settings → Network → **Enable JointSPACE** (sometimes shown simply as “JointSPACE API”).

> Other brands (Sony, etc.) are out of scope for this milestone. The codebase is organized to allow additional plugins later.

---

## Repository Layout

```
src/
  Core/     // TV-agnostic logic, models, Philips plugin, WOL client, config store
  CLI/      // Command line tool (wake, standby)
  Service/  // Windows service (reserved; not required for UI/CLI)
  UI/       // WPF UI (MainWindow, logging, event handlers, tasks helper)
```

---

## Requirements

* **Windows 10/11**
* **.NET 8 SDK** (to build)
* **PowerShell 7+** (optional: only for original PoC scripts)
* **Admin rights** required only to create scheduled tasks from the UI
* TV and PC on the **same network** + **JointSPACE enabled** on the TV

---

## Build

From the repo root:

```bash
dotnet build .\ATVCompanion.sln -c Release
```

Optionally publish (self-contained example):

```bash
dotnet publish .\src\UI\UI.csproj -c Release -r win-x64 --self-contained false -o .\publish\ui
dotnet publish .\src\CLI\CLI.csproj -c Release -r win-x64 --self-contained false -o .\publish\cli
```

Artifacts of a normal build land under each project’s `bin\Release\...` folder.

---

## Run

### UI (recommended)

Run:

```text
src\UI\bin\Release\net8.0-windows\UI.exe
```

**First run & pairing**

1. Enter your **TV IP** and **MAC** (MAC is for Wake-on-LAN).
2. Click **Pair**.
3. TV shows a PIN → enter it in the PC prompt.
4. On success, the app saves:

   * `Ip` (TV IP)
   * `Mac`
   * `DeviceId` (Digest username)
   * `AuthKey` (Digest password)

Saved to:

```text
%ProgramData%\ATVCompanion\Config.json
```

**Daily use**

* **Wake** → sends WOL to MAC (broadcast IP derived from TV IP; override via CLI if needed).
* **Standby** → POST to `/6/input/key` with `{ "key": "Standby" }` using Digest (DeviceId/AuthKey).

The **Console** at the bottom of the window shows info/error log lines.

---

### CLI

Path:

```text
src\CLI\bin\Release\net8.0\CLI.exe
```

Usage:

```text
CLI.exe wake [--mac <MAC>] [--bcast <IP>] [--port <PORT>]
CLI.exe standby [--ip <IP>] [--user <DEVICE_ID>] [--pass <AUTH_KEY>]

Notes:
- Missing flags are loaded from %ProgramData%\ATVCompanion\Config.json (AppConfig.json also accepted).
- 'standby' posts https://<ip>:1926/6/input/key { "key": "Standby" } with Digest auth.
```

Examples (using saved config):

```bash
CLI.exe wake
CLI.exe standby
```

Override ad-hoc:

```bash
CLI.exe wake --mac 00:11:22:33:44:55 --bcast 192.168.1.255
CLI.exe standby --ip 192.168.1.218
```

---

## Configuration

Location:

```text
%ProgramData%\ATVCompanion\Config.json
```

Example:

```json
{
  "Ip": "192.168.1.218",
  "Mac": "00:11:22:33:44:55",
  "DeviceId": "D19BXTCFlqriva8V",
  "AuthKey": "6b9c...a650"
}
```

The CLI accepts both `Config.json` (preferred) and **`AppConfig.json`** for backward compatibility.
Property names are case-insensitive; legacy `device_id` / `auth_key` fields are also recognized.

---

## Scheduled Tasks (Work in Progress)

The **Create Tasks** button will register Windows Task Scheduler entries that call the **CLI** to:

* Wake the TV at a scheduled time
* Put the TV in standby at a scheduled time

**Status:** UI scaffolding is present.
If you see errors like “Unknown command: tasks / create-tasks / install-tasks”, you’re on a CLI without task verbs. Until that lands, create your own scheduled tasks that call the working CLI verbs:

```bash
# Example: wake daily at 07:00
schtasks /Create /TN "ATVCompanion\WakeDaily" ^
  /TR "\"C:\path\to\CLI.exe\" wake" ^
  /SC DAILY /ST 07:00 /RL HIGHEST
```

Or use Task Scheduler GUI and point the action to `CLI.exe wake` or `CLI.exe standby`.

---

## Troubleshooting

**Pair → “404 Not Found”**
You’re likely hitting the wrong API route/version. We use:

```text
https://<ip>:1926/6/pair/request
https://<ip>:1926/6/pair/grant
```

Ensure **JointSPACE v6** is enabled on the TV, and keep the `/6` in the URL.

**Pair → “401 Unauthorized” on grant**
This happens if the HMAC signature or timestamp doesn’t match. The flow:

1. `/pair/request` returns `timestamp` + `auth_key`
2. Compute `auth_signature = Base64( HMACSHA1( secret, $"{timestamp}{pin}" ) )`
3. `/pair/grant` with Digest (username=`DeviceId`, password=`auth_key`)

Retry with a fresh PIN if there’s a delay between request and grant.

**SSL / certificate**
The TV uses a self-signed cert. We deliberately skip certificate validation for TV endpoints. Allowlist the TV if a security product intercepts TLS.

**Wake does nothing**

* Verify MAC format (12 hex digits; `:` or `-` OK)
* Ensure NIC/TV supports WOL and it’s enabled
* If broadcast IP is wrong, pass `--bcast` explicitly

**CLI ignores saved config**

* Ensure `Config.json` exists at `%ProgramData%\ATVCompanion\Config.json`
* Verify it contains the fields shown above
* CLI also checks `%ProgramData%\ATVCompanion\AppConfig.json` and local folder fallbacks

**UI shows nothing / `InitializeComponent` warning**
Typically a mismatched class/namespace between `MainWindow.xaml` and its code-behind. Keep them in sync.

**Build warning `NETSDK1137`**
Safe to ignore for now. We’ll tidy the SDK choice later.

---

## Security & Privacy

* Credentials saved in `%ProgramData%` are **plain JSON** for simplicity.
  If this is a shared machine, restrict ACLs on that folder.
* All TV traffic uses **HTTPS**, but we **skip certificate validation** (self-signed TV cert).

---

## Roadmap

* ✅ Stable Philips JointSPACE v6 pairing, wake, standby
* 🧰 CLI verbs for **task management** (so the UI can rely on them)
* 🔌 Extensible plugin model (additional TV brands)
* 🗒️ More controls (volume, input, app launch) where JointSPACE allows
* 📦 Optional installer/MSI

---

## Contributing

PRs welcome! Please include:

* Clear problem statement
* Repro steps (if a bug)
* Before/after behavior
* Tests or manual test notes where possible

---

## License

*Add your preferred license (e.g., MIT). Until then, assume **All rights reserved**.*

---

## Acknowledgements

* Philips / TP-Vision JointSPACE docs & community notes
* Everyone who tested pairing/WOL and helped land this milestone 🎉
