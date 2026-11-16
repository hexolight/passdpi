# PassDPI

**English** · [Türkçe](README.tr.md)

A lightweight Windows tool that gets around ISP **DPI-based site blocking** using
[GoodbyeDPI](https://github.com/ValdikSS/GoodbyeDPI). Press **Set up** and PassDPI tests
GoodbyeDPI profiles on your connection, picks the one that works, and remembers it per network.

- One-time administrator approval at install; after that it never asks again.
- A small Windows service runs GoodbyeDPI in the background. When idle it uses ~0% CPU
  (event driven, no polling).
- A small window, no tray icon. Closing it exits the app completely.
- Per-network memory: switch Wi-Fi and the right profile is applied (and quickly verified) automatically.
- Turkish and English, dark and light theme.
- No telemetry. The only network traffic is to your test sites and the one-time engine download from GitHub.

## How it works

```
PassDPI.exe (UI, standard user)  ──named pipe──►  PassDPI service (LocalSystem)
                                                     ├─ runs goodbyedpi.exe (+ WinDivert driver)
                                                     ├─ profile tester (HTTPS probes)
                                                     └─ network watcher (NetworkChange events)
```

**Automatic test.** First PassDPI measures your test sites with the engine off. If the control
sites fail, the problem is your Internet connection, not DPI. If everything already opens, nothing
is blocked. Otherwise it tries each profile in turn: start GoodbyeDPI → wait → open HTTPS
connections to the test sites → stop. It stops as soon as a profile opens every site.

A site counts as open only if the TLS handshake succeeds with a certificate valid for that host,
so ISP block pages and poisoned DNS answers count as failures. If no profile helps, the engine stays
**off** and your connection is left exactly as it was.

**Profiles** come from the official GoodbyeDPI README: modesets `-9`, `-5`…`-8` (modern) and
`-1`…`-4` (legacy). Each one is also tried with DNS redirection: Yandex DNS on port 1253, as in
GoodbyeDPI's own `2_any_country_dnsredir.cmd`, and Cloudflare DNS. That makes 27 profiles, tested
from most to least likely to work.

**Network memory.** Results are stored per network. The network key is a salted SHA-256 of the
Wi-Fi SSID, default gateway IP and gateway MAC. SSIDs are never stored in plain text and nothing
is sent anywhere.

## Install

1. Download `PassDPI-<version>-win-x64.zip` from [Releases](../../releases) and extract it.
   Keep `PassDPI.exe` and `PassDpi.Service.exe` together.
2. Run `PassDPI.exe` and click **Install**. Windows asks for administrator approval once.
   - The installer copies PassDPI to `C:\Program Files\PassDPI`.
   - It downloads GoodbyeDPI `0.2.3rc3` from the official GitHub release and checks its SHA-256.
     Alternatively, choose **Install from an existing GoodbyeDPI folder** (only that exact version
     is accepted; it is copied into the protected folder).
   - It registers and starts the `PassDPI` service.
3. Press **Set up**.

Later you can open PassDPI from the Start menu.

> **SmartScreen / antivirus warning.** PassDPI is not code-signed, so Windows SmartScreen may
> say "Windows protected your PC". Click *More info → Run anyway*. Many antivirus products also flag
> GoodbyeDPI and WinDivert as "riskware" or "hacktool", because they intercept network packets.
> This is expected for this kind of tool. Build from source if you prefer.

### Settings

- **Start with Windows.** Switches the service between Automatic and Manual start. When it is
  Manual, opening the app starts the service without asking for admin rights. Only the installing
  user is allowed to start it.
- **Manual mode.** Pin any built-in profile on the *Profiles* tab, or go back to automatic.
- **Custom arguments.** Optional. Only flags documented in the GoodbyeDPI README are accepted, with
  validated values. Flags that take file paths (such as `--blacklist`) are rejected.
- **Test sites.** Editable. Keep at least one *Control* site that is not blocked.
- **Uninstall.** Removes the service, the engine files, settings and network memory.

## Security model

The service runs as LocalSystem, so it is designed to give a standard user nothing to work with:

- Binaries live in `C:\Program Files\PassDPI`, which only Administrators and SYSTEM can write to.
  Data lives in `C:\ProgramData\PassDPI`, which has a protected ACL with no user access. The
  service repairs these ACLs if they are loosened.
- The engine files are SHA-256 verified against pinned hashes at install time **and before every
  start**.
- The control pipe's DACL allows only the installing user (and SYSTEM). Network logons are denied.
  The UI checks that the pipe server is really the PassDPI service process. Requests are a fixed
  command set (`StartTest`, `Apply`, `Stop`, `SetAutoStart`, `SetTestSites`, `SetCustomArgs`,
  `Uninstall`, …). The service never accepts file paths or command lines, and messages are capped
  at 64 KB.
- GoodbyeDPI runs in a Windows job object. If the service dies, the engine dies with it and normal
  networking is restored.
- PassDPI never touches Defender, firewall rules, system DNS settings or any other system setting.

## Build

Requirements: Windows 10/11 x64 and the .NET 8 SDK (newer SDKs work too).

```powershell
dotnet build PassDpi.sln -c Release
dotnet test tests/PassDpi.Tests -c Release
pwsh build/publish.ps1          # → artifacts/PassDPI-<version>-win-x64.zip
```

The service is published as a trimmed, self-contained single file. The WPF app is self-contained
and single-file but not trimmed, because WPF doesn't support trimming.

Developer mode: `PassDpi.Service.exe --console` runs the service in the foreground. It needs an
elevated prompt and an install folder at `C:\Program Files\PassDPI`.

### Project layout

| Project | Role |
|---|---|
| `PassDpi.Core` | Paths, pipe protocol, SCM/ACL helpers, validation, uninstall steps |
| `PassDpi.Engine` | Pinned GoodbyeDPI manifest, download/verify, profiles, flag whitelist, process runner |
| `PassDpi.Probe` | HTTPS probing and the automatic profile tester |
| `PassDpi.Settings` | Settings store, per-network memory, network fingerprint |
| `PassDpi.Service` | Windows service: controller, pipe server, network watcher, log |
| `PassDpi.Setup` | Elevated install/uninstall (run once by the UI with UAC) |
| `PassDpi.App` | WPF UI (TR/EN, dark/light) |
| `tests/PassDpi.Tests` | Unit tests |

## License

PassDPI is MIT licensed (see `LICENSE`). GoodbyeDPI is Apache-2.0 and WinDivert is LGPLv3/GPLv2.
Both are downloaded at install time and are not part of this repository. See
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).

PassDPI is not affiliated with the GoodbyeDPI or WinDivert projects. Check the laws in your country
before using circumvention tools.
