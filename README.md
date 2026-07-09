<p align="center">
  <img src="assets/claudelk-256.png" alt="Claudelk" width="180" />
</p>

# Claudelk

Drive an **ELK-BLEDOM** Bluetooth LED strip from .NET, and (optionally) wire it
to [Claude Code](https://claude.com/claude-code) lifecycle hooks so the strip
reflects whatever Claude is doing — colour, blink, off, whatever you map it to.

Inspired by [claude-lamp](https://github.com/bobek-balinek/claude-lamp) (MIT,
bobek-balinek). Claudelk targets the cheap ELK-BLEDOM strips that ship on
AliExpress, runs on Windows, and is implemented in C# on .NET 10.

> **Status:** CLI and Claude Code integration both shipped. The integration
> runs without a long-running daemon — each hook fires the published exe
> with `async: true` so Claude never blocks on the ~1.6 s BLE write.

## Solution layout

```
Claudelk.slnx
└── src/
    ├── Claudelk.Core/    BLE + protocol library (reusable)
    └── Claudelk.Cli/     claudelk.exe — command-line interface
```

## Requirements

- Windows 10 build 1809 (17763) or later — needed for the WinRT BLE stack
  that [32feet.NET](https://github.com/inthehand/32feet) wraps.
- .NET 10 SDK.
- An ELK-BLEDOM (or compatible: `ELK-BLE`, `LEDBLE`, `MELK`, `ELK-BULB`,
  `ELK-LAMPL`) strip, powered, in range, and **not currently connected** to
  the vendor mobile app.

## Build & publish

```powershell
# Dev build (use 'dotnet run' afterwards):
dotnet build Claudelk.slnx

# Production exe used by Claude Code hooks (~1.6 s cold per command):
dotnet publish src/Claudelk.Cli -c Release
# Default output: src\Claudelk.Cli\bin\Release\<tfm>\publish\
#
# To install elsewhere (e.g. so Claude Code hooks can invoke a stable path),
# create `src/Claudelk.Cli/Claudelk.Cli.csproj.user` — gitignored, picked up
# automatically by MSBuild — with:
#   <Project>
#     <PropertyGroup>
#       <PublishDir>C:\Path\To\Install\</PublishDir>   <!-- pick any path -->
#     </PropertyGroup>
#   </Project>
```

## Use

The examples below assume your publish directory is on `PATH` so `claudelk`
resolves directly. Otherwise substitute the full exe path.

```powershell
# 1. Discover nearby strips.
claudelk scan              # add --debug to see all BLE adverts

# 2. Save a device as the default target. This also runs Windows pair so
#    subsequent commands skip the advert scan and connect in ~1 s.
claudelk pair <device-id>

# 3. Drive it.
claudelk on
claudelk color "#ff8800"
claudelk blink "#ff0000"                       # default 4×250ms, holds colour
claudelk blink "#ff0000" 10 250 --end "#ffffff" # 5s pulse, ends white
claudelk brightness 60
claudelk effect 0x87
claudelk off
```

Every command accepts `--device <id>` to override the default. The saved
device id lives at `%APPDATA%\Claudelk\config.json`.

## Claude Code integration

The integration is purely lifecycle hooks in your user-level
`~/.claude/settings.json` — no daemon process. Each event invokes
`claudelk.exe` with `"async": true` and `"shell": "powershell"` so Claude
Code returns immediately while the strip catches up in the background.

How loud you want the strip to be is entirely up to you. Below are two
**example** configurations to crib from — neither is mandatory.

### Example A — "ambient white, blink when Claude wants me"

The strip glows white whenever Claude Code is open, blinks red for 5 s on
permission prompts, blinks yellow for 3 s when Claude finishes and is
waiting for your next message. Never turns off.

| Event              | Matcher              | Strip state                                  |
|--------------------|----------------------|----------------------------------------------|
| `SessionStart`     | —                    | solid white `#ffffff`                        |
| `Notification`     | `permission_prompt`  | red `#ff0000`, 5 s blink, ends on white      |
| `Notification`     | `idle_prompt`        | yellow `#ffff00`, 3 s blink, ends on white   |

```json
"SessionStart": [{
  "hooks": [{
    "type": "command", "shell": "powershell", "async": true,
    "command": "& 'C:\\path\\to\\claudelk.exe' color '#ffffff'"
  }]
}],
"Notification": [
  {
    "matcher": "permission_prompt",
    "hooks": [{
      "type": "command", "shell": "powershell", "async": true,
      "command": "& 'C:\\path\\to\\claudelk.exe' blink '#ff0000' 10 250 --end '#ffffff'"
    }]
  },
  {
    "matcher": "idle_prompt",
    "hooks": [{
      "type": "command", "shell": "powershell", "async": true,
      "command": "& 'C:\\path\\to\\claudelk.exe' blink '#ffff00' 6 250 --end '#ffffff'"
    }]
  }
]
```

### Example B — strip mirrors every lifecycle phase

The strip changes colour on every event. Closer to claude-lamp's behaviour
but produces a lot of BLE traffic (`PreToolUse` fires on every tool call).

| Event              | Strip state                  |
|--------------------|------------------------------|
| `SessionStart`     | amber `#ffaa00` (ready)      |
| `UserPromptSubmit` | cyan `#00ccff` (thinking)    |
| `PreToolUse` (\*)  | orange `#ff8800` (acting)    |
| `Stop`             | green `#00ff44` (idle)       |
| `Notification`     | blink red `#ff0000`          |
| `SessionEnd`       | off                          |

### Notification matcher values

The `Notification` event fires for several distinct reasons. Useful matchers:

| Matcher              | Meaning                                                 |
|----------------------|---------------------------------------------------------|
| `permission_prompt`  | Claude is asking you to approve a tool call             |
| `idle_prompt`        | Claude finished its turn and is waiting on your reply   |
| `elicitation_dialog` | An MCP server is asking you for structured input        |

Combine with `\|` for regex alternation, e.g. `"matcher": "permission_prompt|idle_prompt"`.

After editing `settings.json`, open `/hooks` in Claude Code once to reload
the config (or start a fresh session).

## Protocol summary

Every command is a 9-byte BLE GATT write to
`0000fff3-0000-1000-8000-00805f9b34fb` on service `0000fff0-...`:

| Action       | Bytes                                            |
|--------------|--------------------------------------------------|
| Power on     | `7e 00 04 01 00 00 00 00 ef`                     |
| Power off    | `7e 00 04 00 00 00 00 00 ef`                     |
| Brightness % | `7e 00 01 NN 00 00 00 00 ef`                     |
| RGB colour   | `7e 00 05 03 RR GG BB 00 ef`                     |
| Effect speed | `7e 00 02 NN 00 00 00 00 ef`                     |
| Built-in fx  | `7e 00 03 CC 03 00 00 00 ef` (CC = 0x80–0x9f)    |
| Temperature  | `7e 00 03 NN 02 00 00 00 ef`                     |

Brightness is only honoured in solid-RGB mode — built-in effects and
grayscale modes ignore it. See `NOTICE.md` for the upstream sources that
documented these byte formats.

## Performance

| Path                                         | Time         |
|----------------------------------------------|--------------|
| `dotnet run --project ...`                   | ~6 s         |
| `claudelk.exe <cmd>` (paired, cold)          | ~1.6 s       |
| Within one process (e.g. `blink`'s 8 writes) | ~30 ms each  |

`Dispatcher.ResolveDeviceAsync` calls `Bluetooth.GetPairedDevicesAsync()`
first and only falls back to a 3 s advert scan if the device is unknown to
Windows — pairing once during `claudelk pair` is what keeps every later
command sub-2-second. Going below the ~1.6 s floor needs a daemon that
holds the GATT connection open across invocations; that work is not done.

## Roadmap

1. ✅ Standalone CLI to scan, pair, and drive an ELK-BLEDOM strip.
2. ✅ Claude Code hooks for `SessionStart`, `UserPromptSubmit`, `PreToolUse`,
   `Stop`, `Notification`, and `SessionEnd`.
3. ⏳ Optional long-running daemon to push per-command latency from
   ~1.6 s to <100 ms. Not needed for the hook flow above; only relevant
   if you want sub-second feedback on every individual tool call.

## Credits

See [`NOTICE.md`](NOTICE.md) for full attribution.

## License

MIT — see [`LICENSE`](LICENSE).
