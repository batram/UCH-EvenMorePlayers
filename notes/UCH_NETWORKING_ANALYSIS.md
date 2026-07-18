# UCH 1.13 Networking Analysis (Spectator Couch Focus)

Analysis of the decompiled 1.13 source at
`C:\Users\mjb\develop\UCH-dev\decompiled_UCH\UCH-decomp_1.13\Assembly-CSharp\`,
written while auditing the spectator-couch work in `Patches/SpectatorMod.cs` /
`Patches/SpectatorHotSeat.cs`. Line numbers refer to the decompiled files.

See also [UCH_LATEJOIN_ANALYSIS.md](UCH_LATEJOIN_ANALYSIS.md) for the facts
discovered while building the late-join feature (connection gates, scene msg 39
+ early-Ready hazard, kick/purge paths, welcome-handshake contents,
non-networked ScoreKeeper, piece replay, Steam joinability).

## 1. Transport & message plumbing

- The game uses **UNet HLAPI** (`UnityEngine.Networking`). `LobbyManager :
  NetworkLobbyManager` (`LobbyManager.cs:14`), `LobbyPlayer : NetworkLobbyPlayer`,
  `GamePlayer : NetworkBehaviour`.
- Custom app messages are defined in **`NetMsgTypes.cs`**: `public static readonly
  short` values computed as `47 + (++msgCount)` — i.e. sequential IDs **48..~105**
  (base 47 = UNet `MsgType.Highest`). Examples: `NetworkClientConnected=48`,
  `GameRuleSet=50`, `CharacterPicked=51`, `LobbyVoting=77`, … `ThwompTriggered`
  last. IDs above the vanilla range still require coordination with other mods;
  no fixed custom ID should be treated as globally safe.
- Handler registration happens in one LobbyManager method (~lines 262–373):
  - Server: `NetworkServer.RegisterHandler(NetMsgTypes.X, distributeServerMessage)`
  - Client: `this.client.RegisterHandler(NetMsgTypes.X, distributeMessage)`
  - `client` is the inherited `NetworkManager.client` property.
- `distributeServerMessage` re-broadcasts incoming messages with
  `NetworkServer.SendToAll(msg.msgType, readMessage(msg))` (`LobbyManager.cs:485`).
  **`SendToAll` includes the host's own local client connection** — the game
  relies on this echo pattern: server is a pure relay, every peer (including the
  host client) applies the message via the client handler. A mod that wants
  host-and-client symmetric behavior should copy this relay pattern rather than
  special-casing `NetworkServer.active`.
- `LobbyManager.OnStartClient(NetworkClient lobbyClient)` (`LobbyManager.cs:731`)
  is the natural hook point to register additional handlers on both sides.

## 2. LobbyPlayer state sync

- Key `[SyncVar]`s (LobbyPlayer.cs ~3049–3116): `networkNumber` (1-based network
  slot), `localNumber` (index on the owning machine), `PickedAnimal`,
  `PlayerColor`, `playerName` (only hooked SyncVar), private `playerStatus`.
- `Status` enum: `INACTIVE=0, CURSOR=1, CHARACTER=2, READY=3, COUCH=4`
  (LobbyPlayer.cs:3227).
- **`PlayerStatus` property is the sync mechanism** (LobbyPlayer.cs:76–90): the
  setter, when `isLocalPlayer || hasAuthority`, issues
  `CmdSetPlayerStatus(value)` (line 952: server just sets the SyncVar) and also
  sets it locally. No hook — remote clients observe changes passively via SyncVar
  deserialization. So *setting `PlayerStatus` on a LobbyPlayer you don't own does
  nothing network-wise and gets overwritten by the next sync.*
- `IsLocalPlayer` (LobbyPlayer.cs:116): `!LobbyManager.instance.IsInOnlineGame ||
  realIsLocalPlayer`. Online, only the owning machine sees `true`.
- Character pick flow: `RequestPickCharacter` → `[Command]
  CmdRequestPickCharacter` (server validates `IsCharacterTaken`) →
  `CmdAssignCharacter` + `[ClientRpc] RpcRequestPickResponse(networkNumber, ok)`.
  On success the owner sets `LocalPlayer.PlayerCharacter` and `PlayerStatus =
  CHARACTER`.
- `CmdPlayerPickedCharacter` (LobbyPlayer.cs:867) forwards
  `PlayerStatus == COUCH` as the `hotseat` bool into
  `LevelSelectController.RpcPlayerPickedCharacter(...)` — but in 1.13 the RPC
  receives the flag and doesn't act on it beyond join-indicator UI.
- `UnpickCharacter()` (805): sets `PlayerStatus = CURSOR`, then
  `CmdSendCharUnpicked`/`CmdSwitchToCursor`/`CmdRemoveCharacter`.

## 3. The couch (HotSeat) is 100% local — and dead in online games

- `HotSeat` (`HotSeat.cs`) is a plain MonoBehaviour, **no networking at all**.
  Fields: `SeatPositions`, protected `Seat[] seats`, protected
  `Dictionary<Controller, Player[]> playerControlMap`, protected
  `List<Character> charactersAtCouch`, private `bool hidden`.
- `Update()` (HotSeat.cs:22): if `LobbyManager.instance.IsInOnlineGame` →
  `hide()`, else `show()`. **`SitPlayer` early-returns when `hidden`**, and
  `IsSeatAvailable()` returns false when hidden — so the entire couch feature is
  inert online by design.
- `SitPlayer(Player)` (76): seats the character (position, `Ready=true`,
  `Sitting=true`, sorting layer `"Default2"`, adds to
  `playerControlMap[player.UseController]`).
- Vanilla couch entry: `LevelSelectController.ReceiveEvent` (1221–1330). Player
  resolution loops `JoinedPlayers[num]` matching `lobbyPlayer2.IsLocalPlayer &&
  e.Sender.ControlsPlayer(lobbyPlayer2.localNumber)`. On Accept at couch with
  `Status.CHARACTER`, not in menu, seat available:
  `SitPlayer(GetPlayer(num + 1))`, forces game mode via
  `PartyModeButton.SimulatePress(true)` + `Lock()`, sets
  `GameState.UsingHotSeat = true`, then `lobbyPlayer2.PlayerStatus = COUCH`
  (which does Cmd-sync the status), and **spawns an additional local Player**
  sharing the same controller (`HotseatPlayer = true`). This extra-player +
  forced-PARTY behavior is exactly what a spectator mod must *not* trigger.
- Re-entry restore: `setupController(LobbyPlayer)` (1709–1778) re-seats
  `Status.COUCH` players when the lobby reloads. Cleanup:
  `OnLobbyPlayerObjectDestroyed` (2298) unsits/promotes and clears
  `UsingHotSeat` when seats empty.

## 4. Game start: lobby → game player mapping

- `GameControl.SetupStart(GameState.GameMode)` (`GameControl.cs:526`), with
  `protected Queue<GamePlayer> PlayerQueue` (3245; public accessor
  `CurrentPlayerQueue`).
  - Host path (`hasAuthority`): iterates PlayerTracker slots; **any player with
    `PickedAnimal == NONE` is removed** (`LobbyPlayer.PlayerStatus = INACTIVE`,
    `RemovePlayer()`); others get Character+Cursor spawned with client authority
    and are enqueued.
  - Client path: enqueues existing GamePlayers with `PickedAnimal > NONE`;
    local players lacking a `GameNetID` are removed via
    `ClientScene.RemovePlayer`.
  - Final validation dequeues/requeues, destroying entries without
    `CharacterInstance`/`lobbyPlayer`; zero valid local players →
    `AbortGameInProgress`.
- `VersusControl.SetupStart` (`VersusControl.cs:242`) then assigns turn order and
  scoreboard slots assuming `PlayerQueue.Count` matches lobby bookkeeping;
  `LobbyManager.GetLobbyPlayer(gamePlayer.networkNumber)` null → logged error,
  and count mismatches are where the observed `IndexOutOfRangeException` /
  "disconnected during setup" came from.
- `LevelSelectController.LaunchLevel` (824+): before the scene loads, the host
  **removes LobbyPlayers with `PickedAnimal == NONE`** from the lobby.
- Number spaces: `PlayerManager.GetPlayer(int)` is **1-based and indexed by
  local number** (`Player.Number == localNumber`), *not* networkNumber.
  `GamePlayer` carries both `networkNumber` and `localNumber` SyncVars.
  Confusing these two spaces was the root cause of most "Player is null for
  player 2" failures in the earlier spectator attempts.

## 5. No native spectator support

- The only `Spectator` type in the game is cosmetic (the audience-sprite shown
  for dead/finished players, driven by `VersusControl` via
  `CharacterInstance.SpectatorImage` and `LevelLayout.SpectatorStart/Goal`).
  There is no networked spectate role.
- A LobbyPlayer that never picks a character is actively **purged at game start**
  (LaunchLevel + SetupStart paths above). A spectator implementation must either
  (a) keep the spectator's `PickedAnimal` valid and filter them out of
  `PlayerQueue`/scoreboard/turn logic everywhere counts are assumed equal, or
  (b) let the game treat them as absent and protect them from removal /
  reconnect churn. Option (a) is what the current mod attempts; the mismatch
  points in §4 are the ones that must be covered consistently on *both* host and
  clients.

## 6. Implications for the spectator mod (delta vs. current code)

- Current `SpectatorMod.cs` uses custom `MessageBase` IDs 1010-1011 — this is a
  temporary allocation pending ecosystem-wide ID coordination; ID choice
  avoids the vanilla range but is not globally collision-proof (§1). The
  send/receive topology is asymmetric (server applies
  sit/unsit only, clients only update a dictionary) instead of the game's own
  relay-and-apply-everywhere pattern, so seat visuals desync across peers.
- Setting `lobbyPlayer.PlayerStatus = COUCH` for *remote* players (e.g. in
  `SyncSpectatorStatus`) is a no-op per §2 — only the owner or server authority
  can effectively change it.
- Replacing the `HotSeat` component at runtime (destroy + `AddComponent
  <SpectatorHotSeat>`) forfeits the serialized couch trigger wiring and the
  vanilla local-couch feature; a Prefix on `HotSeat.Update`/`SitPlayer` that
  un-hides and permits sitting online (plus blocking the PARTY-forcing branch in
  `LevelSelectController.ReceiveEvent`) works *with* the existing component
  instead.
- `NetworkServer.SendToAll` echoes to the host's local client (§1) — host code
  paths that both "apply locally" *and* receive their own broadcast will
  double-apply unless the handler is idempotent.
