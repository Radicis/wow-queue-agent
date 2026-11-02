# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed
- CI now requires pull requests to include an entry under `[Unreleased]`.

## [1.0.2] - 2026-11-02

### Fixed
- Windows agent no longer treats a standalone `Reconnect` label as a disconnection; reconnect
  screens require accompanying `Log Out` text, avoiding false disconnects on recognized game screens.

## [1.0.1] - 2026-11-02

### Changed
- Windows agent: skip redundant OCR when the captured window frame hasn't changed, reusing the last
  observation so state debouncing still advances without reprocessing static frames.
- Windows agent: limit the local WebSocket server to 4 concurrent clients, enforce a proper close
  handshake, and reject unexpected incoming messages since the agent connection is read-only.

## [1.0.0] - 2026-11-02

### Added
- Windows tray agent (`WowQueueAgent.exe`): captures the WoW window, reads the login dialog with Windows OCR
  and classifies it as logged in, in queue (position and time remaining), disconnected, error or WoW not running.
- "Queue soon" alert when the estimated time drops under 10 minutes (configurable).
- Android app (`WowQueue.apk`): live queue time and state, foreground service and local notifications for
  logged in, disconnected, error, queue soon and PC unreachable.
- Pairing window with QR code, large PC address and port, and a built-in fake WoW window for testing.
- Automatic discovery via mDNS and reconnect when the PC's IP changes.
- Configurable detection patterns in `patterns.json`.
