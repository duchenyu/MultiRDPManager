# MultiRDPManager

> Multi-window RDP remote desktop manager — built on FreeRDP with batch connection, group control, and thumbnail monitoring

## Features

- **Multi-session management** — Connect to multiple Windows servers simultaneously, manage via the left sidebar, preview in the central panel
- **Bulk import** — Import servers from CSV (Name, IP, Username, Password) with one click
- **Group control mode** — Synchronize mouse/keyboard input to all slave sessions, with master switching
- **Thumbnail panel** — Real-time thumbnail monitoring of all connected sessions, with search filtering
- **Dark theme** — Modern dark UI designed for server administration

## Tech Stack

- **WPF** (.NET 8, win-x64)
- **FreeRDP** — [RoyalApps.Community.FreeRdp.WinForms](https://github.com/royalapplications/royalapps-community-freerdp) v2.0
- **Windows Global Hooks** — `WH_MOUSE_LL` / `WH_KEYBOARD_LL` for group control input forwarding

## Quick Start

### 1. Download & Run

Download `MultiRDPManager.FreeRDP.exe` from the [Releases](../../releases) page. It's a self-contained single file — no .NET runtime required.

### 2. Build from source

```bash
git clone https://github.com/duchenyu/MultiRDPManager.git
cd MultiRDPManager/src/MultiRDPManager.FreeRDP
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Usage

1. Click **New** or **Import** to add servers (CSV bulk import supported)
2. Select a server and click **Connect** (or **Connect All**) — thumbnails appear on the right
3. Interact with the remote desktop directly in the main preview area
4. Check multiple servers → click **Group** → select a master
5. In group control mode, mouse/keyboard actions are forwarded to all checked slave sessions

## Screenshot

<div align="center">
  <img src="docs/screenshot.png" width="800" alt="Screenshot">
</div>

## License

MIT
