# WordCraft guest matchmaking and relay

Standalone .NET 8 server; no simulation, database, Unity, or NuGet packages.
Run `dotnet run --project WordCraft.Server.csproj` (localhost:5080).
Run boundary checks with `dotnet run --project Checks/Checks.csproj`.
Build container with `docker compose up --build -d` from this folder.

## HTTP contract

All JSON uses camelCase. All mutation requests use `Content-Type: application/json`.
`protocolVersion` and `contentVersion` are positive uint32 values supplied by the
game build. Only exact version pairs match. These values establish compatibility,
not trust: guests can claim any versions.

| Method | Path | Request | Response |
| --- | --- | --- | --- |
| GET | `/health` | none | `{ "status": "ok" }` |
| POST | `/rooms` | version object | host ticket, waiting |
| POST | `/rooms/{roomCode}/join` | version object | guest ticket, matched |
| POST | `/queue` | version object | ticket, waiting or matched |
| GET | `/session` | bearer token | current ticket |
| DELETE | `/session` | bearer token | 204; cancel both peers if matched |
| GET | `/relay` | WebSocket upgrade, bearer token | binary datagram relay |

Version object: `{ "protocolVersion": 1, "contentVersion": 123 }`.
Example ticket (seed is a decimal **string**, parsed as ulong by the game):

```json
{
  "playerToken": "64 uppercase hexadecimal characters",
  "roomCode": "10HEXCHARS",
  "matchId": "32 hexadecimal characters",
  "peerId": 0,
  "state": "matched",
  "protocolVersion": 1,
  "contentVersion": 123,
  "seed": "12345678901234567890"
}
```

Waiting tickets have null matchId and seed `"0"`. Queue tickets have null
roomCode. Rooms are case insensitive. Host is peer 0, joiner peer 1. Both peers
must use the shared seed and match metadata. Poll `/session` once per 2 seconds
while waiting, then connect the relay. There is no start barrier at the relay;
the existing lockstep protocol establishes peer readiness. Tokens are random
256-bit secrets; store only for the current session and never put them in URLs.
Use `Authorization: Bearer TOKEN` for HTTP and WebSocket, supported by .NET's
`ClientWebSocket.Options.SetRequestHeader` on Unity desktop.

Errors use `{ "message": "description" }`: 400 invalid input, 401 expired or
invalid token, 404 missing/full room, 409 incompatible versions/not matched/
duplicate socket, 429 request limit, 503 capacity. HTTP binding failures also
return 400; clients must tolerate an empty error body. Unknown routes use 404.
Cancel/disconnect invalidates both tokens; there is no reconnect/resume.

## Relay and operating limits

- One binary WebSocket message equals one game UDP datagram, unchanged.
- Nonempty messages up to 65,536 bytes; fragmented messages are reassembled.
- Text, oversized payloads, more than 256 messages/second, or full output queues
  terminate the entire match. Each player has at most 32 queued datagrams.
- Max 128 guests across queue, rooms, and matches. HTTP body cap 4 KiB.
- Per socket-address HTTP rate limit: 120 requests/minute, no waiting queue.
- Waiting sessions expire after 5 minutes. Both peers must attach within 60
  seconds of pairing. Matches expire after 2 hours. Cleanup runs every 10 seconds
  and on lobby access. Disconnect or explicit cancellation closes both peers.

Single instance only. Restart loses all sessions; rolling deploys disconnect
matches. Do not add replicas without shared coordination and relay routing.
No accounts, ranking, cheat prevention, authoritative simulation, persistence,
spectators, or authentication beyond possession of guest tokens.

## Public deployment

Put a TLS reverse proxy in front of port 5080 and expose HTTPS/WSS, including
WebSocket Upgrade forwarding. Keep the origin private (the compose example
binds localhost). The public URL must be configured in the game client. Allow
long-running WebSocket connections and keep proxy idle timeout above 20 seconds.
Never expose plaintext tokens to the public internet. The server intentionally
does not trust forwarded IP headers: behind a proxy its rate limit is shared by
that proxy's clients. Configure per-client limits at the trusted proxy when
serving more users; do not blindly accept forwarded headers from the internet.
The 512 MiB compose cap is a starting limit, not a measured capacity guarantee;
load-test your infrastructure before inviting public traffic. Managed TLS,
DNS, deployment credentials, monitoring and incident response are operator work.
