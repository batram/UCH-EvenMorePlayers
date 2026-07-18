# Late Join — Plan & Implementation Status

Feature: join a running modded lobby at any time (invite code **and** lobby
browser), as **spectator** or **active player**. Active joiners drop in at the
next PLACE phase; match scores sync to the joiner via a custom snapshot message.
The spectator-couch system (being fixed in a parallel effort) is only touched
through the delegate bridge in `Patches\LateJoin\LateJoinSpectatorBridge.cs`.

Full approved plan (verified game facts with file:line refs, patch list 1-18,
message schemas, milestones, risks R1-R9):
`latejoin_plan.md`
Line refs below: game decomp `...\decompiled_UCH\UCH-decomp_1.13\Assembly-CSharp\`,
UNet decomp `...\UCH-decomp_1.13\com.unity.multiplayer-hlapi.Runtime\`.

Build without killing/launching the game:
`dotnet build EvenMorePlayers.csproj -v q -p:PreBuildEvent= -p:PostBuildEvent=`

## Design in one paragraph

The only join gates are in UNet `NetworkLobbyManager.OnServerConnect`/
`OnServerAddPlayer` (scene != "TreeHouseLobby" -> refuse); UCH's overrides just
call base. The Steam lobby stays joinable mid-match (only hidden via
`data_matchProgress`). A late client connects, defers `ClientScene.Ready` until
the level scene announced by scene msg 39 is loaded (with `onlineScene = null`
vanilla would Ready in the wrong scene), UNet auto-spawn then delivers all
networked objects + SyncVars. A mod Hello/welcome handshake (msg IDs 1001-1006)
fills the non-replicated gaps: game mode/rules/outfits (vanilla msgs unicast),
phase/round (1002), ScoreKeeper snapshot (1003, ScoreKeeper is NOT networked),
placed-piece replay (vanilla MsgPiecePlaced unicasts). Kick/purge paths
(`CmdIShouldNotBeHere`, `DisconnectBrokenClients`, unpicked-player purges,
`ServerChangeScene` lobby-return destroy) are suppressed for protected joiners.
Activation of a "play"-mode joiner happens at the next `VersusControl.ToPlaceMode`
on the host (spawn GamePlayer/Character/Cursor) + relay msg 1004 applied
idempotently on every peer (PlayerQueue tail, TurnOrder = Count-1, scoreboard/
partybox/inventory-book insertion). Late-join detection client-side =
`Matchmaker.CurrentMatchmakingLobby.GetMatchProgress() != 0` (no custom lobby
data needed; progress resets to 0 whenever the party is back in the treehouse).

## Status by milestone

### DONE — M0 skeleton/config (builds clean)
- `EvenMorePlayers.csproj`: added `<Publicize Include="com.unity.multiplayer-hlapi.Runtime" />`.
- `MorePlayersMod.cs`: config `lateJoinEnabled` (true), `lateJoinMode`
  ("play"|"spectate"), `lateJoinAutoPick` (true), `lateJoinKeepVisible` (true).
- New: `Patches\LateJoin\LateJoinState.cs` (shared state incl. PendingConnections,
  ProtectedNumbers, PendingPicks, JoinerModes, LastGameState, Reset()),
  `LateJoinMessages.cs` (ids 1001-1006 + MessageBase classes),
  `LateJoinSpectatorBridge.cs` (OnLateJoinerWantsSpectate / IsSpectating delegates —
  spectator side wires these; late-join never references spectator internals).

### DONE — M1 connection gates (builds clean; NOT yet game-tested)
`Patches\LateJoin\LateJoinConnectionPatches.cs`:
- `NetworkLobbyManager.OnServerConnect` prefix-reimpl: drop scene gate, keep
  maxPlayers gate, register PendingConnections (also when treehouse+progress!=0
  = countdown window).
- `NetworkLobbyManager.OnServerAddPlayer` prefix-reimpl minus scene early-out
  (FindSlot/msg-45 logic preserved; adds joiner to ProtectedNumbers).
- `LobbyManager.DisconnectBrokenClients` prefix: scrubs pending conns from
  `connectionLifetimes`/`brokenClientConnections` for up to 90s.
- `LobbyManager.OnClientConnect` prefix: when `MatchInProgress()` and not host,
  sets `ClientJoiningLate`, runs `OnLobbyClientConnect`+`CallOnClientEnterLobby`,
  skips the premature Ready/AddPlayer (post-scene-load `OnClientSceneChanged`
  does both — NetworkManager.cs:1321-1343).
- Kick suppression via prefixes on `LobbyPlayer.CallCmdIShouldNotBeHere`
  (client) and `CmdIShouldNotBeHere` (host, ProtectedNumbers). NOTE: deviates
  from plan patch 5 (no `OnLobbyClientSceneChanged` reimpl needed).
- State `Reset()` postfixes on `LobbyManager.OnStopClient` +
  `NetworkLobbyManager.OnStopHost` (exists at NetworkLobbyManager.cs:573).

### DONE — M2 treehouse integration (builds clean; NOT yet game-tested)
- `NetworkLobbyManager.ServerChangeScene` prefix-reimpl (same file): on lobby
  return, skip Destroy+Replace when the connection's player object still IS the
  LobbyPlayer (GamePlayer-less joiner); replicates NetworkManager.ServerChangeScene
  body (SetAllClientsNotReady, networkSceneName, LoadSceneAsync, SendToAll(39)).
- `Patches\LateJoin\LateJoinActivation.cs` (so far only purge guards):
  prefix on `NetworkLobbyPlayer.RemovePlayer` and on `ClientScene.RemovePlayer`
  blocking removal of protected/late-local joiners (covers GameControl.SetupStart
  host+client purges and LaunchLevel unpick-removal).

### DONE — M3 welcome packet + scores (builds clean; NOT yet game-tested)
`Patches\LateJoin\LateJoinWelcome.cs`:
- Handler registration postfix on `LobbyManager.OnStartClient` (server: Hello;
  client: GameState 1002, Scores 1003).
- Joiner sends Hello (coroutine from `LobbyPlayer.Start` postfix, waits for
  isLocalPlayer + networkNumber + netId; skips if treehouse — vanilla
  ClientLoadedTreehouse handshake covers that case).
- Host Hello handler unicasts: MsgSwitchToMode, MsgApplyRuleset (replica of
  LevelSelectController.SendAllRules, LevelSelectController.cs:2855-2880), AFK
  MsgGameRuleSet, per-character MsgCommunicateCharacterOutfits, 1002 snapshot
  (phase/round from FindObjectOfType<GameControl>), 1003 scores, and a placed-
  piece replay: one vanilla MsgPiecePlaced per `GameControl.placedBlocks` entry
  with `PlayerNumber` = host's networkNumber (applied on the joiner by its copy
  of the host's PiecePlacementCursor, PiecePlacementCursor.cs:1236-1270).
- Score snapshot build/apply: reads/writes publicized
  `ScoreKeeper.Instance.playerTotal` (Dictionary<GamePlayer, scoreInfo>);
  client applies via coroutine waiting up to 30s for GamePlayers to resolve.
- Score refresh: postfix on `ScoreKeeper.TallyPointBlockAllPlayers` re-unicasts
  1003 to every waiting joiner (JoinerModes keys).
- Protection teardown: postfix on `LobbyPlayer.DoCharacterPickedEvent` (879)
  removes ProtectedNumbers/JoinerModes entry, sets ClientIntegrated for the
  local joiner.

### DONE — M4 active mid-level drop-in (builds clean; NOT yet game-tested)
Implemented in `LateJoinActivation.cs` exactly per the plan below, plus:
- `LateJoinWelcome.SendHelloWhenReady` calls `LateJoinActivation.SendPickRequest`
  right after Hello when mode=play (animal always -1 = auto-pick for now).
- Handlers 1004/1006 (client) and 1005 (server) registered via an own
  `LobbyManager.OnStartClient` postfix.
- Host `ToPlaceMode` prefix skips picks whose LobbyPlayer/connection vanished.
- 1004 insertion coroutine waits <=30s for GamePlayer + Character + Cursor +
  VersusControl; idempotent via `PlayerQueue.Contains` / `playerTotal.ContainsKey`.
- Spectator handoff: `LevelSelectController.SetupLobbyAfterWait` postfix; null
  bridge delegate -> drop protection, become normal lobby player; otherwise a
  coroutine polls `IsSpectating` before releasing protection/ClientIntegrated.
Original work plan (kept for reference):
1. **Pick flow (1005/1006)**: joiner (mode=play) sends PickRequest after Hello
   (animal = -1 for auto-pick). Host validates against `lobbySlots[*].PickedAnimal`
   + PendingPicks, picks first free animal, sets `lp.NetworkPickedAnimal` and
   `lp.NetworkplayerStatus = CHARACTER` (server-side SyncVar writes propagate;
   wrapper names verified: LobbyPlayer.cs:1241), stores PendingPicks, broadcasts
   PickResult. Cannot reuse `CmdRequestPickCharacter` (NREs on
   `CurrentLevelSelectController` mid-level, LobbyPlayer.cs:901-911).
2. **Host activation** — prefix on `VersusControl.ToPlaceMode` (VersusControl.cs:416),
   hasAuthority only, BEFORE original body, for each pending pick:
   - GamePlayer: Instantiate `LobbyManager.instance.gamePlayerPrefab`, copy
     NetworknetworkNumber/NetworklocalNumber/NetworkPickedAnimal +
     characterOutfitsList from LobbyPlayer, `PlayerTracker.AddGamePlayer`,
     `NetworkServer.ReplacePlayerForConnection(conn, gpObj, playerControllerId)`
     (mirrors LobbyManager.cs:868-891 + NetworkLobbyManager.cs:185-223).
   - Character + Cursor: exact copy of GameControl.SetupStart host block
     (GameControl.cs:583-604): Instantiate gc.CharacterPrefab/gc.CursorPrefab,
     set Network* SyncVars, `NetworkServer.SpawnWithClientAuthority(x, gpObj)`,
     `gp.CallCmdAssignCharacter/CallCmdAssignCursor`. Use `global::Cursor`
     (name clash with UnityEngine.Cursor).
   - Do NOT enqueue here; broadcast 1004 `NetworkServer.SendToAll` instead.
3. **1004 handler (all peers incl. host echo, idempotent)**: coroutine waits for
   GamePlayer + CharacterInstance + CursorInstance (fields GamePlayer.cs:1376-1388)
   of that networkNumber, then: if not already in `gc.PlayerQueue` -> enqueue at
   tail, `TurnOrder = Count-1`; `CursorInstance.UseCamera =
   gc.MainCamera.GetComponent<Camera>()` (VersusControl.cs:348); VersusControl:
   `graphScoreBoardInstance.SetPlayerCount` + re-run `SetPlayerCharacter` per
   queue index (VersusControl.cs:351-358); PARTY: `partyBoxInstance.SetPlayerCount`
   + on authority `AddPlayer(networkNumber, animal)` + CallCmdAssignCursor to the
   PartyPickCursor (VersusControl.cs:360-368); CREATIVE: `RemainingPlacements
   [n-1] = CreativePiecesPerRound` (VersusControl.cs:497-502); ScoreKeeper
   playerTotal entry if missing; joiner machine only: `invBookInstance.AddPlayer
   (localNumber, networkNumber, LocalPlayer.UseController, CharacterSprite)` +
   `((PiecePlacementCursor)CursorInstance).InventoryBookMenu = invBookInstance`
   (VersusControl.cs:262-263), `gp.Control.AddReceiver(gc)`,
   `cursor/character.SetLocalController(gp.Control)` (GameControl.cs:606-611);
   finally clear ProtectedNumbers/JoinerModes/PendingPicks for that number, set
   ClientIntegrated on the joiner.
   Register 1004/1005/1006 handlers via an own `LobbyManager.OnStartClient`
   postfix in LateJoinActivation.cs (multiple postfixes are fine).
4. **Spectator handoff**: joiner with mode=spectate reaching the treehouse ->
   invoke `LateJoinSpectatorBridge.OnLateJoinerWantsSpectate(networkNumber)`,
   keep protection until `IsSpectating` true; null delegate -> normal unpicked
   lobby player (hook: `LevelSelectController.SetupLobbyAfterWait` postfix).

### DONE — M5 browser visibility (builds clean; NOT yet game-tested)
`Patches\LateJoin\LateJoinBrowser.cs`:
- `MatchmakingLobby.SetLobbyVisible` prefix-return-false for visible==false
  while NetworkServer.active + lateJoinKeepVisible (re-show passes through).
- `SteamMatchmakingLobby.SetLobbyJoinable` prefix forcing joinable=true under
  the same condition (covers the AFK joinability toggles).
- `PickableNetworkButton.SetSearchResultInfo` postfix (MorePlayersMod.cs) now
  appends a "▶" marker when `lobbyInfo.matchProgress != 0` and late join is on.

### DONE — Docs
`notes\UCH_LATEJOIN_ANALYSIS.md` written (all facts from the plan's
Documentation section, with decomp file:line refs) and cross-linked from
`notes\UCH_NETWORKING_ANALYSIS.md`.

## Implementation complete — next step is game testing (see below)

## Testing (none done yet — needs the real game)
Windows host + Mac client (csproj post-build deploys to both; logs:
`S:\SteamLibrary\...\Ultimate Chicken Horse\output_log.txt` and
`\\192.168.2.113\...\BepInEx\LogOutput.log`; set `fullDebug=true`).
Per-milestone test scripts are in the plan file. First checks (M1): joiner
survives >=2 min mid-level, no "Spawn scene object not found" spam (risk R1),
placed pieces appear at correct transforms (calibrates R2). Watch risks R3
(cross-peer queue insertion order), R5 (NetworkSurrogate binding on
thwomp/hockey levels), R6 (AFK kick of idle spectators), R9 (vote-kick with
GamePlayer-less joiner). V1 scope: VersusControl modes (CREATIVE/PARTY) only —
gate activation on `GameSettings.GameMode` (risk R7).
