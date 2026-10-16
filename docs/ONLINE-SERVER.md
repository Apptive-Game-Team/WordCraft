# Online server

The standalone guest matchmaking and WebSocket relay lives in [`Server/`](../Server/README.md).
Its README defines the HTTP contract, authentication, limits, local integration
checks, container deployment, and single-instance limitations.

It pairs two peers with identical protocol/content versions, gives both a shared
seed and peer role, and forwards the existing lockstep datagrams over WebSockets
so players do not need inbound UDP ports. Simulation remains on the clients.

This is a runnable first deployment component, not a hosted public service.
Public use requires a provisioned HTTPS/WSS endpoint and client configuration.

## Play locally

1. From the repository root run `dotnet run --project Server/WordCraft.Server.csproj`.
   Alternatively run `docker compose up --build -d` inside `Server/`.
2. Open two game clients. Select **ONLINE**, choose a faction, and use
   `http://localhost:5080` on both clients.
3. Select **FIND OPPONENT** on both, or **CREATE PRIVATE ROOM** on one and
   enter its code with **JOIN ROOM** on the other.
4. **CANCEL** removes a waiting seat. Disconnecting ends that match for both
   players. To play again, return to the start screen and search again.

For different computers, deploy the server behind HTTPS and enter the same public
server URL in each game. The client accepts plaintext HTTP only for loopback.
There is no publicly hosted default server yet.

The standalone private repository is
[Apptive-Game-Team/WordCraft-Server](https://github.com/Apptive-Game-Team/WordCraft-Server).
`Server/` in this game repository is the integration snapshot; changes must be
synced deliberately to the standalone repository.

## Verification

- `dotnet run --project Server/Checks/Checks.csproj`: launches a temporary real
  HTTP/WebSocket server and checks matching, versions, authentication, payload
  limits, cancellation, disconnection, capacity and expiry.
- With a local server running, `dotnet run --project Host -- onlinecheck
  http://127.0.0.1:5080`: pairs two game peers, sends player commands, compares
  their hashes after 400 ticks, and checks disconnect propagation.
- A built player accepts `-server http://127.0.0.1:5080 -faction Humans` for
  quick matching; add `-room CODE` to join a private room.

Guest tokens remain in memory, never in URLs or saved preferences. The endpoint
URL is saved for convenience. Queue polling is every two seconds.
