# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
