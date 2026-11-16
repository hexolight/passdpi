# Third-party notices

PassDPI's own source code is licensed under the MIT License (see `LICENSE`).

PassDPI does **not** include the following components in this repository or in its
release archive. At install time it downloads them from the official GoodbyeDPI GitHub
release (`0.2.3rc3`, file `goodbyedpi-0.2.3rc3-2.zip`), verifies their SHA-256 hashes,
and places them in `C:\Program Files\PassDPI\engine`. The upstream license files
shipped in that release are copied to `C:\Program Files\PassDPI\engine\licenses`.

---

## GoodbyeDPI

- Project: https://github.com/ValdikSS/GoodbyeDPI
- Copyright: ValdikSS and GoodbyeDPI contributors
- License: Apache License 2.0 — https://www.apache.org/licenses/LICENSE-2.0
- Used as: the `goodbyedpi.exe` engine, run unmodified as a child process.

GoodbyeDPI itself bundles code under these licenses (see its `licenses/` folder):
`uthash` (BSD revised) and `getline` (BSD-style).

## WinDivert

- Project: https://reqrypt.org/windivert.html — https://github.com/basil00/WinDivert
- Copyright: basil00 (Basil) and contributors
- License: GNU Lesser General Public License v3 **or** GNU General Public License v2
  (dual licensed; see `LICENSE-windivert.txt` in the GoodbyeDPI release)
- Used as: `WinDivert.dll` and the signed kernel driver `WinDivert64.sys`, loaded by
  `goodbyedpi.exe`. PassDPI does not link against WinDivert.

## .NET

- Microsoft .NET runtime and libraries (bundled in the self-contained executables)
- License: MIT — https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- `System.ServiceProcess.ServiceController` NuGet package — MIT
