# UCH Late-Join Analysis (game facts)

Facts about UCH 1.13 / UNet discovered while building the late-join feature.
Companion to [UCH_NETWORKING_ANALYSIS.md](UCH_NETWORKING_ANALYSIS.md); the design
that uses these facts lives in [latejoin_plan.md](latejoin_plan.md) and
[LATEJOIN_STATUS.md](LATEJOIN_STATUS.md).

Line refs: game decomp `C:\Users\mjb\develop\UCH-dev\decompiled_UCH\UCH-decomp_1.13\Assembly-CSharp\`,
UNet decomp `C:\Users\mjb\develop\UCH-dev\decompiled_UCH\UCH-decomp_1.13\com.unity.multiplayer-hlapi.Runtime\`.

## Connection gates live only in the UNet base class

`NetworkLobbyManager.OnServerConnect` (NetworkLobbyManager.cs:323-352)
disconnects any connection when `SceneManager.GetSceneAt(0).name !=
m_LobbyScene` ("TreeHouseLobby"), and `OnServerAddPlayer` (371-415) silently
returns on the same check. UCH's `LobbyManager` overrides add **no** gating of
their own — they just call `base` (LobbyManager.cs:770-774). Neutralize those
two checks and a client can connect at any time.

## Scene delivery is free; early Ready is the hazard

The server sends `MsgType.Scene` (39) with `networkSceneName` to every new
connection (NetworkManager.cs:1022-1026), and the client loads it via
`ClientChangeScene`/`FinishLoadScene` (898-923). But UCH sets
`onlineScene = null` (LobbyManager.cs:100), so `OnClientConnectInternal`
(NetworkManager.cs:1093-1108) calls `OnClientConnect` → `ClientScene.Ready` +
`AddPlayer(0)` **immediately, before the level scene is loaded** — producing
"Spawn scene object not found" mismatches. Deferring Ready works because the
post-load `OnClientSceneChanged` (NetworkManager.cs:1321-1343) performs both
Ready and AddPlayer itself.

## Kick / purge paths that hit a characterless late client

- `LobbyManager.OnLobbyClientSceneChanged` (LobbyManager.cs:963-1000; kick
  branch 989-999): a client that finds itself characterless in a level scene
  self-reports via `CmdIShouldNotBeHere` (LobbyPlayer.cs:1001-1005) and the
  host kicks it.
- `LobbyManager.DisconnectBrokenClients` (LobbyManager.cs:166, 1595-1669)
  kills any connection without a Lobby/GamePlayer after ~3 seconds — far less
  than a mid-level scene load takes.
- `GameControl.SetupStart` purges unpicked players at round start: host branch
  via `LobbyPlayer.RemovePlayer()` (GameControl.cs:559-580), client branch via
  direct `ClientScene.RemovePlayer` for local players without a GameNetID.
- `LevelSelectController.LaunchLevel` removes unpicked players before the
  scene change.

## ServerChangeScene breaks GamePlayer-less connections on lobby return

`NetworkLobbyManager.ServerChangeScene` (NetworkLobbyManager.cs:439-463)
destroys each connection's current player object and
`ReplacePlayerForConnection`s the stored LobbyPlayer. For a joiner whose
player object *is* still the LobbyPlayer (never got a GamePlayer), this
destroys the LobbyPlayer itself and breaks the client.

## What UNet auto-spawn does and does not replicate

A newly-Ready client receives all networked objects (LobbyPlayers,
GamePlayers, Characters, Cursors, GameControl) with current SyncVars. **Not**
covered, because they travel via unbuffered messages:

- Game mode / ruleset / outfits — only sent by the treehouse-only
  `ClientLoadedTreehouse` handshake (LevelSelectController.cs:214-216,
  2656-2694; rules via `SendAllRules`, 2855-2880).
- Phase transitions — RPCs, unbuffered.
- Piece placements — pieces are placed via relay `MsgPiecePlaced` messages,
  not SyncVars. A host can replay them by unicasting one vanilla
  `MsgPiecePlaced` per `GameControl.placedBlocks` entry with the host's own
  `PlayerNumber`; the joiner's copy of the host's `PiecePlacementCursor`
  applies them (PiecePlacementCursor.cs:1236-1270).
- `ScoreKeeper` — a plain non-networked singleton
  (`Dictionary<GamePlayer, scoreInfo> playerTotal`, ScoreKeeper.cs:8-22,
  429-448); scores need a custom snapshot message.
- `NetworkSurrogateSpawned` link messages (GameControl.cs:820-830) — sent
  once; moving hazards (thwomps, hockey pucks) may not bind for a late
  joiner (open risk).

## Late joiners never enter the vanilla GamePlayer path

Msg 44 (sceneLoaded) is only sent from a real `SceneManager.sceneLoaded`
event (NetworkLobbyPlayer.cs:124-135). A client already in the level when it
connects never fires it, so GamePlayer creation for a late joiner is entirely
under mod control (mirroring `OnLobbyServerSceneLoadedForPlayer`,
LobbyManager.cs:868-891, and `SceneLoadedForPlayer`,
NetworkLobbyManager.cs:185-223).

## TurnOrder is per-peer, not synced

`GamePlayer.TurnOrder` is a plain int recomputed on every peer from queue
order (VersusControl.cs:344-375). Any insertion into `GameControl.PlayerQueue`
must therefore run identically (same position) on every machine — hence a
relay message applied everywhere rather than host-only state.

## Mid-level character picks can't reuse the vanilla flow

`CmdRequestPickCharacter` dereferences
`LobbyManager.instance.CurrentLevelSelectController` (LobbyPlayer.cs:901-911),
which is null outside the treehouse → NRE. Server-side writes to
`NetworkPickedAnimal` / `NetworkplayerStatus` (LobbyPlayer.cs:1225-1252)
propagate as SyncVars and are the safe substitute.

## Steam layer: lobbies stay joinable mid-match

Leaving the treehouse only calls `SetLobbyVisible(false)` +
`SetMatchProgress(100)` (LevelSelectController.cs:692, 705-706) — the Steam
lobby is never made unjoinable, so invite codes work mid-match in vanilla
already. The lobby browser lists in-progress lobbies too, sorted last
(SteamLobbySearchList.cs:143-160), and `Matchmaker.LobbyListInfo` carries
`matchProgress` for labeling. Client-side late-join detection needs no custom
lobby data: `Matchmaker.CurrentMatchmakingLobby.GetMatchProgress() != 0`
(progress resets to 0 whenever the party returns to the treehouse). The AFK
paths (UnityMatchmaker.cs:212-213, 472-474) do toggle
`SetLobbyJoinable(false)` temporarily.

## Message plumbing conventions

Vanilla message IDs occupy 48..~105; late join currently claims 1001-1006 and
spectator couch temporarily uses 1010-1011. Handlers are registered in a
`LobbyManager.OnStartClient` postfix. Relay messages follow the vanilla
`distributeServerMessage` → `SendToAll` topology (LobbyManager.cs:483-493);
`SendToAll` echoes to the host's own local client, so relay handlers must be
idempotent.
