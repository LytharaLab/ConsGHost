# Verification

Run the checks from the repository root. Each release should identify which checks ran against its own build.

## Tests without Garry's Mod

```powershell
./test.ps1
```

The script builds CGH, runs CTest and the managed test runner, then opens the desktop host for its UI self-check. The controlled game fixture exercises x64 startup, .NET hosting, managed and native entry points, the `GameFrame` adapter, game-thread dispatch, cross-process messaging, dependency order, addon staging, mod configuration, and cleanup after failed startup or a worker crash. It also tests spaces and Unicode in installation paths.

The UI check covers navigation, mod search, settings persistence, both config editors, and the extra DLLs page. It checks the five compiled translation catalogs, automatic language matching, live switching, and the saved choice on the next startup. It also checks that host log messages stay in English. It renders the WinForms controls to images under `artifacts/ui-check/`. Integration results are stored under `artifacts/tests/`.

## Garry's Mod smoke test

Close any running Garry's Mod session, sign in to Steam, and pass the folder that contains `gmod_win64.exe`:

```powershell
$gameDirectory = Read-Host 'Garry''s Mod installation directory'
./test.ps1 -RealGame -GameDirectory $gameDirectory
```

Use `-SkipBuild` after building the same source revision. The test starts `gm_construct` with `-noaddons -noworkshop` for that game process and verifies at least 300 `GameFrame` callbacks, game-thread dispatch, service calls, cross-process messages, and session cleanup. It uses the `ServerGameDLL009` interface. Record the tested game build and branch with the result. This test leaves Steam launch settings and installed addons in place.

The fixture is useful for regression testing. A real-game result applies to the tested build, map, and mod set. Addon combinations and later Garry's Mod builds can behave differently.

## Checking an extracted release

Run these commands from the extracted release folder:

```powershell
./ConsGHost.Runtime.exe --version
./ConsGHost.Runtime.exe --verify-version .
./cgh_mrt_win64.exe --version
```

Compare the reported version with the release tag and `version.json`. To check the download before extraction, run `Get-FileHash -Algorithm SHA256 -LiteralPath <zip-file>` and compare its hash with the first field in the matching `.sha256` file.
