# WoW Queue Monitor

Watches the World of Warcraft login-queue dialog on a Windows PC and shows the queue time on your
phone (same Wi‑Fi), with notifications for: **logged in**, **disconnected**, **error**, and
**queue under ~10 minutes**.

```
windows/   C# .NET 8 tray agent: window capture → OCR → state classifier → WebSocket server
android/   React Native app: foreground service + WebSocket client + notifications
docs/      protocol.md
scripts/   release helpers
```

## Download

Grab the latest files from the [Releases](https://github.com/Radicis/wow-queue-agent/releases) page:
`WowQueueAgent-vX.Y.Z.exe` (PC) and `WowQueue-vX.Y.Z.apk` (Android). Verify them against `SHA256SUMS.txt`.
Put the exe in its own folder (it creates its config files next to itself). Windows SmartScreen may warn
because the exe isn't code-signed: choose "More info" then "Run anyway". On Android, allow "install unknown apps".
See [CHANGELOG.md](CHANGELOG.md) for what changed.

## Windows agent

Single self-contained `.exe`, no installer or runtime needed (Windows 10 1903+; English OCR language).

1. Build: `windows\publish.ps1` → `dist\windows\WowQueueAgent.exe` (needs the .NET 8 SDK).
2. Run it. A tray icon appears; it creates `config.json` and `patterns.json` next to the exe.
3. Allow the Windows Firewall prompt (private networks) so the phone can connect.
4. The pairing window (QR code, PC address and port) opens on launch; set `ShowPairingOnStart` to `false` in
   `config.json` to disable that. Reopen it any time by double-clicking the tray icon.
5. Run WoW **windowed or borderless**. Exclusive fullscreen can't be captured (agent shows "No usable frame").

Only the centre of the WoW window is OCR'd (`CropX/Y/Width/Height` in `config.json`) so chat text can't
cause false alerts. `agent.log` records state changes with the OCR text, useful for tuning.

### Tuning detection
Dialog wording differs between versions/servers. Edit the regexes in `patterns.json`
(`Disconnected`, `Error`, `QueueIndicators`, `QueuePosition`, `QueueEta`) and restart the agent.
"Logged in" means *no recognised modal*, so the login screen without a dialog counts as logged in.

Tests: `dotnet test windows` · Try it without WoW via "Launch test app" (pairing window or tray menu),
which opens a fake WoW window with Queue / Disconnected / Error / Logged-in buttons. Inspect the raw
messages with `windows\tools\ws-listen.ps1`.

## Android app

Requires Node 22+, JDK 17 and the Android SDK (not needed on the PC running the agent).

```
cd android
npm install
npm test
cd android; .\gradlew assembleRelease      # APK in app\build\outputs\apk\release
```
(The release build is signed with the debug key by default; sideload via "install unknown apps".)
For a quick dev run: `npx react-native run-android`.

First launch: allow notifications, scan the QR code (or enter IP/port). Tap
"Allow background running" so Android doesn't kill the monitor. The app runs a foreground service
holding the WebSocket; notifications are local (no cloud). If the PC's IP changes the app re-finds it via mDNS.

Note: Android 15 limits `dataSync` foreground services to ~6 h per launch; reopen the app after that.

## Releasing

Versions follow [SemVer](https://semver.org/) and come from the git tag; nothing else needs editing.

1. In `CHANGELOG.md`, rename `## [Unreleased]` content into a new `## [X.Y.Z] - YYYY-MM-DD` section (the release notes are taken from it).
2. Commit, then tag and push: `git tag vX.Y.Z; git push origin main vX.Y.Z`
3. The **Release** workflow tests, builds the exe and APK, and publishes a GitHub release with
   `WowQueueAgent-vX.Y.Z.exe`, `WowQueue-vX.Y.Z.apk` and `SHA256SUMS.txt`. Tags with a suffix (`v1.1.0-beta.1`) are marked pre-release.

The tag sets the exe version and the APK `versionName`; the APK `versionCode` is `major*10000 + minor*100 + patch`.
The **CI** workflow runs tests and builds on every push and pull request.

### One-time: Android signing key
Updates only install over an earlier version if signed with the same key.
Run `scripts\create-android-keystore.ps1`, back up the keystore file privately (never commit it), and add the
four `ANDROID_*` secrets it prints to the repo (Settings ? Secrets and variables ? Actions). Without them the
release still builds but is signed with the debug key and a warning is shown.
