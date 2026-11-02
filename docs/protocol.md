# Protocol (v1)

The Windows agent hosts a WebSocket server. The phone connects as a client.

```
ws://<pc-ip>:<port>/ws
```

- No authentication: anyone on the same network can connect and read the queue state. It is read-only.
- Plain `ws://`, LAN only.
- Default port `47821`, configured in `config.json`.
- Discovery: the agent advertises `_wowqueue._tcp` over mDNS (service name = PC name).
- Pairing payload (QR code and pairing window):
  `{"v":1,"hosts":["192.168.1.20"],"port":47821,"name":"PC"}`

All messages are JSON text frames, agent → phone. The phone sends nothing.

## `hello` (once, on connect)
```json
{"type":"hello","v":1,"agent":"MYPC","bootId":"2d23788b"}
```

## `event` (live, plus replay of the last 10 minutes on connect)
```json
{"type":"event","v":1,"id":"2d23788b:1","kind":"QUEUE_SOON","message":"…","at":"2026-11-02T10:16:37Z"}
```
`id` is unique per agent run; the phone de-duplicates by it (and persists seen ids).

| kind | fired when |
|---|---|
| `LOGGED_IN` | a recognized post-login screen is visible (for example, character select/create or gameplay ruleset) |
| `DISCONNECTED` | disconnect dialog detected |
| `ERROR` | other error dialog detected (patterns in `patterns.json`) |
| `NO_CLIENT` | WoW window disappeared |
| `QUEUE_SOON` | ETA fell to the threshold (default 10 min); re-arms if ETA rises above 1.5× threshold or the queue is left |

The phone itself raises `AGENT_OFFLINE` when it has lost the agent for 30 s.
Events are not fired for the very first state seen after the agent starts (except `QUEUE_SOON`).

## `state` (on every observation, at least every ~3 s — doubles as heartbeat)
```json
{"type":"state","v":1,"state":"QUEUE","position":42,"etaSeconds":540,
 "etaUpdatedAt":"…","soon":true,"since":"…","observedAt":"…"}
```
`state` is one of `UNKNOWN`, `NO_CLIENT`, `LOGGED_IN`, `QUEUE`, `DISCONNECTED`, `ERROR`.
`UNKNOWN` is reported when OCR does not match a known state. The agent OCRs the center crop every
poll and performs a full-window OCR fallback at most once every five seconds while the state is
unknown, so post-login labels near the screen edges can still be recognized.
`position`/`etaSeconds` are only present for `QUEUE`. `etaSeconds` is the value WoW displays (it is
coarse, minutes), `etaUpdatedAt` is when it last changed. The phone treats >10 s of silence as a dead link.
