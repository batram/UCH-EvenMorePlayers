# Ultimate Chicken Horse Mod Agent Guide

## Project context

- Projects covered by this guide are mods for *Ultimate Chicken Horse* version
  **1.13**.
- Mods are **BepInEx 5** plugins written in C# for **.NET Framework 4.8**.
- A mod may use **Harmony** for runtime patches, but do not introduce Harmony when
  the requested functionality can use the game's public lifecycle directly.
- Decompiled *Ultimate Chicken Horse* 1.13 source may be available locally as a
  reference; its location is machine-specific (see "Local paths" below).
- Use the decompiled source as the primary reference when investigating game types,
  methods, fields, behavior, initialization order, networking, and suitable patch
  points. Treat it as read-only reference material unless the user explicitly asks
  to edit it.

## Local paths (machine-specific, not in git)

- Machine-specific paths live in `EvenMorePlayers.user.props`, which the csproj
  imports if present and which is gitignored (`*.user.props`). Read it to find:
  - `UCHfolder` — the game installation directory (falls back to the default
    Steam path if the file is missing).
  - `DecompFolder` — the decompiled UCH 1.13 source tree, if available.
- The game log is written to `$(UCHfolder)\output_log.txt`.
- Game and BepInEx assembly references resolve through `UCHfolder` in the project
  file; the build also copies the plugin DLL into `$(UCHfolder)\BepInEx\plugins\`.
- Inspect the current project rather than assuming its assembly name, plugin ID,
  output directory, entry point, dependencies, or deployment behavior.

## Access to private members (Krafs.Publicizer)

- The project uses the **Krafs.Publicizer** NuGet package (see `EvenMorePlayers.csproj`)
  to make all members of `Assembly-CSharp` and `InControl` public at compile time:
  ```xml
  <PackageReference Include="Krafs.Publicizer" Version="1.0.1" />
  <Publicize Include="Assembly-CSharp" />
  <Publicize Include="InControl" />
  ```
- This means private/internal/protected game fields, methods, and types can be
  accessed **directly in C#** — no reflection, `AccessTools`, `Traverse`, or
  Harmony `AccessTools.Field` helpers are needed for these assemblies.
- At runtime the game assemblies are unchanged; the publicized references are
  compile-time only, and the IL access works because the .NET runtime does not
  re-verify accessibility here.

## Working conventions

- Compare target method signatures and control flow against the decompiled 1.13
  source before changing Harmony patches or relying on game internals.
- Preserve compatibility with the BepInEx, Harmony, Unity, networking, and game
  assemblies referenced by the current project.
- Prefer focused patches and normal game lifecycle calls over copying substantial
  decompiled game logic.
- Keep mods standalone. Do not add source, project, build, configuration, or runtime
  dependencies between sibling mods unless the user explicitly requests them.
- Preserve unrelated user changes and untracked files in every repository.
- Do not commit decompiled proprietary game source or game assemblies.
