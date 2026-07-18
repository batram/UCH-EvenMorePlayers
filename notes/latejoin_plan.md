# Late Join a Running Lobby — EvenMorePlayers mod (UCH 1.13)

## Context

UCH rejects any client connecting once the party has left the treehouse: the UNet base class `NetworkLobbyManager` disconnects connections when the active scene isn't the lobby scene. UCH's own `LobbyManager` overrides add no gating — they just call `base`. The goal is a late-join feature for the EvenMorePlayers mod: a player can join a modded lobby **at any time**, including mid-round in a level, and enter either as a **spectator** or as an **active player** (spawned into the running game at the next build-phase boundary). Discovery works via invite code **and** the lobby browser. Match scores sync to the joiner. All peers in a modded lobby run the mod (the "Play More" version-fudge guarantees this), so both host- and client-side patches are available.

The spectator-couch system (`Patches\SpectatorMod.cs`, temporary msg IDs 1010-1011) is separate from late join — late-join must stay in **separate new files** and touch it only through one delegate-based bridge.

Line refs: game = `C:\Users\mjb\develop\UCH-dev\decompiled_UCH\UCH-decomp_1.13\Assembly-CSharp\`; UNet = `C:\Users\mjb\develop\UCH-dev\decompiled_UCH\UCH-decomp_1.13\com.unity.multiplayer-hlapi.Runtime\`.

## Key verified facts the design rests on

- **Gates**: `NetworkLobbyManager.OnServerConnect` (NetworkLobbyManager.cs:323-352) disconnects when `SceneManager.GetSceneAt(0).name != m_LobbyScene` ("TreeHouseLobby"); `OnServerAddPlayer` (371-415) silently returns on the same check. The max-players gate is already neutralized by existing ctor patches (MorePlayersMod.cs:356-363, 411-426).
- **Scene delivery works for free**: server sends `MsgType.Scene` (39) with `networkSceneName` to every new connection (NetworkManager.cs:1022-1026); client loads it via `ClientChangeScene`/`FinishLoadScene` (898-923).
- **Early-Ready hazard**: UCH sets `onlineScene = null` (LobbyManager.cs:100), so on connect `OnClientConnectInternal` (NetworkManager.cs:1093-1108) immediately runs `OnClientConnect` → `ClientScene.Ready` + `AddPlayer(0)` **before the level scene is loaded** → scene-object spawn mismatches. Must defer Ready until after scene load; `OnClientSceneChanged` (NetworkManager.cs:1321-1343) then does Ready+AddPlayer itself.
- **Kick guards**: `LobbyManager.OnLobbyClientSceneChanged` (LobbyManager.cs:963-1000, kick branch 989-999) self-reports via `CmdIShouldNotBeHere` (LobbyPlayer.cs:1001-1005) → host kicks characterless players in level scenes. `LobbyManager.DisconnectBrokenClients` (LobbyManager.cs:166, 1595-1669) kills connections without a Lobby/GamePlayer after ~3s.
- **Treehouse-return breaker**: `NetworkLobbyManager.ServerChangeScene` (439-463, verified) destroys each connection's current player object and `ReplacePlayerForConnection`s the LobbyPlayer — for a joiner whose player object *is* the LobbyPlayer (no GamePlayer), this destroys it and breaks the client.
- **Auto-spawn covers a lot**: existing LobbyPlayers/GamePlayers/Characters/Cursors/GameControl and their SyncVars replicate to a newly-ready client. Not covered: ruleset/mode/outfits (only sent via the treehouse-only `ClientLoadedTreehouse` handshake, LevelSelectController.cs:214-216, 2656-2694), phase RPCs (unbuffered), piece placements (relay msgs, not SyncVars), `ScoreKeeper` (plain singleton dict, ScoreKeeper.cs:8-22, 429-448), deferred `NetworkSurrogateSpawned` link msgs (GameControl.cs:820-830).
- **No vanilla GamePlayer path fires for a late joiner**: msg 44 (sceneLoaded) is only sent from a real `SceneManager.sceneLoaded` event (NetworkLobbyPlayer.cs:124-135) — GamePlayer creation is entirely under mod control.
- **Activation point**: `VersusControl.ToPlaceMode` (VersusControl.cs:416+) recomputes party boxes and shuffle from `PlayerQueue.Count`; characters are disabled there; `GamePlayer.TurnOrder` is a plain int computed per-peer (GamePlayer.cs:1425, VersusControl.cs:344-375) so insertion must run identically on every peer.
- **Mid-level pick can't reuse vanilla flow**: `CmdRequestPickCharacter` NREs on `CurrentLevelSelectController` outside the treehouse (LobbyPlayer.cs:901-911) → mod message pair instead.
- **Steam layer**: lobby stays joinable mid-match; hiding is only `SetLobbyVisible(false)`+`SetMatchProgress(100)` at countdown (LevelSelectController.cs:692, 705-706); the browser already lists in-progress lobbies sorted last (SteamLobbySearchList.cs:143-160).
- **Msg plumbing**: vanilla IDs 48..103. Late-join uses **1001-1006** and spectator couch now temporarily uses **1010-1011**. Register in `LobbyManager.OnStartClient` postfix. Relay msgs must copy the `distributeServerMessage` → `SendToAll` → apply-everywhere topology (LobbyManager.cs:483-493); `SendToAll` echoes to the host's local client → handlers must be idempotent. A shared ecosystem allocation scheme remains TODO in Glorpy knowledge.

## New code layout

All new files under `Patches\LateJoin\` (no edits to SpectatorMod.cs/SpectatorHotSeat.cs):
- `LateJoinState.cs` — static state: `ClientJoiningLate`, `PendingConnections`, `ProtectedNumbers` (network numbers exempt from purge/kick), pending picks.
- `LateJoinMessages.cs` — the 6 `MessageBase` classes + ID constants.
- `LateJoinConnectionPatches.cs` — gate/kick/ready patches.
- `LateJoinWelcome.cs` — handler registration, hello/welcome/score sync, piece replay.
- `LateJoinActivation.cs` — mid-level pick + activation at ToPlaceMode + purge protection.
- `LateJoinBrowser.cs` — visibility/joinability patches.
- `LateJoinSpectatorBridge.cs` — the only spectator touch point.

Config (add in `MorePlayersMod.Awake`, MorePlayersMod.cs:30-42): `lateJoinEnabled` (true), `lateJoinMode` ("play"|"spectate"), `lateJoinAutoPick` (true), `lateJoinKeepVisible` (true).

csproj: add `<Publicize Include="com.unity.multiplayer-hlapi.Runtime" />` (EvenMorePlayers.csproj ~69-72) so reimplemented base methods can access `m_PendingPlayers`, `lobbySlots`, etc. directly.

Messages:
| ID | Name | Direction | Payload |
|---|---|---|---|
| 1001 | MsgLateJoinHello | client→host | networkNumber, requestedMode |
| 1002 | MsgLateJoinGameState | host→joiner | sceneName, phase, roundNumber, gameMode, partyBox, placementTimer |
| 1003 | MsgLateJoinScores | host→joiner | per player: networkNumber, totalScore, win/loseStreak, disconnected |
| 1004 | MsgLateJoinActivate | relay (SendToAll) | networkNumber, animal, outfits[] |
| 1005 | MsgLateJoinPickRequest | client→host | networkNumber, animal |
| 1006 | MsgLateJoinPickResult | relay | networkNumber, animal, ok |

## Patch list

### A. Connection gates (`LateJoinConnectionPatches.cs`)
1. `NetworkLobbyManager.OnServerConnect` (NetworkLobbyManager.cs:323-352) — Prefix-replace: keep maxPlayers check, drop the scene check; mark conn in `PendingConnections` when mid-level. (Patching base covers UCH's pass-through override at LobbyManager.cs:770-774.)
2. `NetworkLobbyManager.OnServerAddPlayer` (371-415) — Prefix-replace without the scene early-out (keep per-connection cap, `FindSlot`/msg-45 logic → vanilla `OnLobbyServerCreateLobbyPlayer` at LobbyManager.cs:777-793 assigns networkNumber/color).
3. `LobbyManager.DisconnectBrokenClients` (1595-1669) — Prefix: scrub `PendingConnections` entries from `connectionLifetimes`/`brokenClientConnections` until their LobbyPlayer exists (mod timeout ~90s to allow level-scene load).
4. `LobbyManager.OnClientConnect` (749-753) — Prefix-return-false when `ClientJoiningLate`: run the `OnLobbyClientConnect`/`CallOnClientEnterLobby` bookkeeping (NetworkLobbyManager.cs:613-618) but skip `NetworkManager.OnClientConnect`'s premature `ClientScene.Ready`/`AddPlayer`; the post-load `OnClientSceneChanged` does both.
5. `LobbyManager.OnLobbyClientSceneChanged` (963-1000) — Prefix-replace skipping the `CallCmdIShouldNotBeHere` branch for late joiners.
6. `LobbyPlayer.CmdIShouldNotBeHere` (LobbyPlayer.cs:1001-1005) — host-side Prefix-return-false for `ProtectedNumbers` (belt-and-braces).
7. `NetworkLobbyManager.ServerChangeScene` (439-463) — Prefix-replace: in the lobby-return loop, skip Destroy+Replace when the connection's current player object already **is** the LobbyPlayer; still reset `readyToBegin`. Postfix (host): write `MP_lateJoinScene` lobby data via `Matchmaker.CurrentMatchmakingLobby.SetLobbyData` so joiners know pre-connect they're late-joining.

### B. Welcome packet & scores (`LateJoinWelcome.cs`)
8. `LobbyManager.OnStartClient` (LobbyManager.cs:731-746) — Postfix registering server+client handlers for 1001-1006 (relay types re-broadcast via `SendToAll`).
9. Client sends 1001 once its local LobbyPlayer exists (hook `LobbyPlayerCreatedEvent`/`LocalPlayerAddedEvent` on the GameEventManager bus — pattern per Patches\Handicapper.cs), guarded by `ClientJoiningLate` + scene != TreeHouseLobby.
10. Host on Hello, unicast to `netMsg.conn`:
    - vanilla msgs reusing stock client handlers: `SwitchToMode`, `SetGameModeLock`, AFK `GameRuleSet`, per-character `CommunicateCharacterOutfits` (the treehouse-handshake subset of LevelSelectController.cs:2664-2692);
    - 1002 (phase/round/mode snapshot; joiner sets local `GameControl.Phase`/`nextPhase` to observe);
    - 1003 (scores; joiner writes `ScoreKeeper.Instance.playerTotal` in a coroutine that waits for GamePlayer objects, cf. ScoreKeeper.cs:37-57);
    - piece replay: per entry of `GameControl.placedBlocks` a stock `NetMsgTypes.PiecePlaced` unicast (+ `PieceDestroyed` for removed pieces), one frame after the spawn flush — stock handlers (LobbyManager.cs:324,329) apply them.
11. Host re-unicasts 1003 at each `ReadyToTallyPoints` while the joiner is inactive (idempotent) so activation at any boundary starts from correct totals.

### C. Activation (`LateJoinActivation.cs`)
12. Pick: 1005 → host validates against `lobbySlots[*].PickedAnimal` + pending picks, sets `NetworkPickedAnimal`/`characterOutfitsList` server-side (SyncVars propagate) → 1006 relay. `lateJoinAutoPick` picks the first free animal without UI.
13. `VersusControl.ToPlaceMode` (VersusControl.cs:416) — host Prefix: for picked pending joiners, before the original body: create GamePlayer as `OnLobbyServerSceneLoadedForPlayer` does (instantiate `gamePlayerPrefab` LobbyManager.cs:862-865, copy fields 881-889, `PlayerTracker.AddGamePlayer`, `NetworkServer.ReplacePlayerForConnection`); spawn Character+Cursor with client authority exactly as the `GameControl.SetupStart` host block (GameControl.cs:583-604); then broadcast 1004.
14. 1004 handler (all peers incl. host echo, idempotent): coroutine waits for the GamePlayer/Character/Cursor of that networkNumber, then: enqueue `GameControl.PlayerQueue` tail + `TurnOrder = Count-1`; `graphScoreBoardInstance.SetPlayerCount`/`SetPlayerCharacter` (VersusControl.cs:351-375); ScoreKeeper entry if missing; lives display slot; PARTY `partyBoxInstance` counts (VersusControl.cs:339-343, 360-369); CREATIVE `RemainingPlacements` (497-502); on the joiner's machine only: `invBookInstance.AddPlayer` + controller wiring (VersusControl.cs:252-266, GameControl.cs:606-611).
15. Purge protection while undecided/spectating: Prefix-return-false on `NetworkLobbyPlayer.RemovePlayer` (NetworkLobbyPlayer.cs:144-154) for `ProtectedNumbers` — covers both the `GameControl.SetupStart` host purge (GameControl.cs:559-580) and `LevelSelectController.LaunchLevel`'s unpicked-removal. Protection clears when the joiner picks or hands off to spectator.

### D. Browser / joinability (`LateJoinBrowser.cs`)
16. `MatchmakingLobby.SetLobbyVisible` (MatchmakingLobby.cs:56) — Prefix-return-false for `visible == false` while host + `lateJoinKeepVisible` (call site LevelSelectController.cs:705).
17. `SteamMatchmakingLobby.SetLobbyJoinable` (SteamMatchmakingLobby.cs:514-519) — Prefix forcing joinable under the same condition (covers AFK paths UnityMatchmaker.cs:212-213, 472-474).
18. Match progress: leave vanilla (in-progress lobbies already list, sorted last); extend the existing `PickableNetworkButton.SetSearchResultInfo` postfix (MorePlayersMod.cs:612-619) to label modded lobbies "in progress – joinable".

### E. Spectator bridge (`LateJoinSpectatorBridge.cs`)
Single touch point; spectator effort wires it from its side (one line):
```csharp
public static class LateJoinSpectatorBridge {
    public static Action<int /*networkNumber*/> OnLateJoinerWantsSpectate; // null = spectator off
    public static Func<int, bool> IsSpectating;                            // optional query
}
```
A joiner with `lateJoinMode == spectate` stays inert in-level; on reaching the treehouse, late-join code invokes the delegate and keeps them in `ProtectedNumbers` until `IsSpectating` is true; null delegate → fall back to normal unpicked lobby player.

## Milestones (each testable: Windows host + Mac client, logs per notes\tech_details.md)

- **M0** csproj publicize + skeleton files + config — builds, deploys, no behavior change.
- **M1 inert mid-level observer** — patches 1-6. Join by invite code mid-round: joiner loads the level scene, sees live characters, survives ≥2 min. Log-check: `Spawn scene object not found` spam (early-Ready), and whether placed pieces appear at correct transforms (calibrates how much piece replay is needed).
- **M2 treehouse integration** — patches 7, 15. Joiner idles out the round; on `fadeToLobby` (GameControl.cs:1458-1482) lands in the treehouse, gets a cursor, the vanilla `ClientLoadedTreehouse` handshake delivers mode/rules, picks and plays the next game fully vanilla. Verify no LobbyPlayer destruction and no purge while unpicked.
- **M3 welcome packet + scores** — patches 8-11. Join a match with established scores → scoreboard identical on host/old client/joiner; piece replay verified.
- **M4 active mid-level drop-in** — patches 12-15. `lateJoinMode=play`, join during PLAY phase → at next PLACE phase gets character+cursor, appears on scoreboard/turn order, places a block, plays the run. Watch for the historic VersusControl IndexOutOfRange and PARTY box count mismatches. Test CREATIVE and PARTY; 3+ machines if possible.
- **M5 browser visibility** — patches 16-18. Modded in-progress lobby stays listed and joinable from the second machine's browser; browser join exercises the M1-M4 path.

## Risks / verification focus

- **R1** Early-Ready ordering: if patch 4 isn't enough, further delay `ClientScene.Ready` until `activeScene == networkSceneName`.
- **R2** Piece transforms/destroyed pieces on late spawn — M1 visual diff; unicast replay is the fallback for everything.
- **R3** Cross-peer PlayerQueue insertion order: 1004 and `RpcStartPhase` share the reliable channel, but the wait-for-spawn coroutine adds skew — insertion must complete before the next phase uses the queue; test with induced latency.
- **R4** Lobby-ready accounting (msg 43, NetworkLobbyManager.cs:241-268) with a joiner mid-level — verify no stray `OnLobbyServerPlayersReady`.
- **R5** `NetworkSurrogateSpawned` link msgs missed by joiner (GameControl.cs:820-830) — moving hazards may not bind; fix by host caching + unicasting on Hello. Test on a thwomp/hockey level in M1.
- **R6** AFK auto-kick of an idle spectating joiner — exempt `ProtectedNumbers` if needed.
- **R7** Scope: v1 targets VersusControl (CREATIVE/PARTY); CHALLENGE and FREEPLAY are out — gate activation on `GameSettings.GameMode`.
- **R8** Host-echo double-apply — all relay handlers (1004/1006) idempotent.
- **R9** Vote-kick arrays with a GamePlayer-less joiner — exercise once in M2.

## Documentation (user requested)

New `notes\UCH_LATEJOIN_ANALYSIS.md`, cross-linked from `notes\UCH_NETWORKING_ANALYSIS.md`, capturing the newly discovered game facts:
- Scene msg 39 delivery to new connections + the early-Ready hazard from `onlineScene = null`.
- The two base-class connection gates; UCH overrides add none.
- `CmdIShouldNotBeHere` kick path and `DisconnectBrokenClients` ~3s timing.
- `ServerChangeScene` lobby-return destroy/replace and how it breaks GamePlayer-less connections.
- `ClientLoadedTreehouse` welcome-packet contents (treehouse-only).
- `ScoreKeeper` is non-networked (dict shape, `Setup()` source).
- Pieces = networked objects + relay-message placement; replay-by-unicast trick; deferred `NetworkSurrogate` link messages.
- Steam: lobby stays joinable mid-match; hiding is `SetLobbyVisible(false)` only; browser already lists in-progress lobbies.
- `TurnOrder` is per-peer computed (not a SyncVar); msg 44 only fires from real scene loads, so late joiners never enter the vanilla GamePlayer path.

## Verification (end-to-end)

Build (`dotnet build` — post-build deploys to `S:\SteamLibrary\...\BepInEx\plugins\` and the Mac at `\\192.168.2.113\...`, and launches the game). Two-instance manual test per milestone as listed above; primary diagnostics are `S:\SteamLibrary\steamapps\common\Ultimate Chicken Horse\output_log.txt` and the Mac `LogOutput.log`, with `fullDebug=true` for UNet verbosity.
