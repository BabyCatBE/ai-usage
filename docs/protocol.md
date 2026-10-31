# Phone link protocol (v1)

Goal: the phone can read the PC's usage snapshot from anywhere, with no account, no database and no inbound ports — and nobody in the middle (including the relay operator) can read it.

## Pairing

The PC creates 128 random bits (the *secret*) per phone and shows them as:

- a QR / link: `usage://pair?k=<base32 secret>&r=<relay url>&n=<pc name>`
- a grouped code: `ABCDE-FGHIJ-KLMNO-PQRST-UVWXYZ` (26 base32 characters)

Unused pairings expire after 15 minutes. Removing a device on the PC deletes its secret (revocation).

## Derivation

```
room = hex( HKDF-SHA256(secret, salt = empty, info = "usage/v1/room", 16 bytes) )
key  =      HKDF-SHA256(secret, salt = empty, info = "usage/v1/key",  32 bytes)
```

The relay only ever sees `room`.

## Transport

Both sides open an outbound WebSocket: `wss://<relay>/r/<room>?role=host|client`.
The relay (see `relay/worker`) forwards text frames host → clients and client → host. It keeps nothing, limits frames to 64 KiB and 120 msgs/min, and at most 8 clients per room. It emits plaintext control frames `{"relay":"host_online|host_offline|client_joined"}`.

## Messages

Every data frame is `{"v":1,"n":<base64 nonce 12 B>,"c":<base64 ciphertext||tag>}`, AES-256-GCM with
`AAD = room + ":" + role` (`h` for host→phone, `c` for phone→host) so a frame cannot be reflected.

Plaintext JSON always has `t` (type) and `ts` (unix ms; frames older/newer than 10 minutes are rejected):

| `t` | from | meaning |
|---|---|---|
| `get` | phone | send me the current snapshot |
| `refresh` | phone | collect now (host rate-limits to one per 20 s) |
| `snapshot` | PC | `{ "busy": bool, "d": <snapshot> }` |

## Snapshot (schemaVersion 2)

```json
{
  "schemaVersion": 2, "generatedAt": "2026-10-31T01:00:00+00:00",
  "providers": [{
    "id": "codex", "name": "ChatGPT", "status": "ok|stale|error", "fetchedAt": "...", "errorCode": null, "plan": "Plus",
    "windows": [{ "id": "five_hour|seven_day|monthly|…", "label": "5h", "usedPercent": 12, "resetsAt": "...", "durationMinutes": 300 }],
    "models":  [{ "name": "kimi", "usedPercent": null, "spentUsd": 3.1 }],
    "details": [{ "label": "credits", "value": "0" }]
  }]
}
```

Values above 100 % are kept as reported. `stale` means the last good reading is shown because the latest attempt failed (`errorCode` says why).

## Threat model notes

- A relay operator learns that a room exists, when it is active and message sizes — not contents.
- Anyone who knows `room` can connect but cannot decrypt, forge (GCM) or replay beyond the 10-minute window.
- The secret is stored in plain text in the app's private storage on each side (user profile / Android app sandbox, backups disabled).
