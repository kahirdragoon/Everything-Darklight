# CLAUDE.md

This file provides guidance to Claude Code when working with code in this repository.

Read About/About.xml to get a description of the mod.

DO NOT GUESS, ASK IF UNSURE

## Project Overview

**Everything Darklight** is a RimWorld 1.7 mod by kahirdragoon.
Package ID: `kahirdragoon.everythingdarklight`
Def prefix: `EDL_`

Requires Ideology: vanilla only offers `CompGlower`'s darklight toggle gizmo while Ideology is active.

Split out of Custom Fixes (`kahirdragoon.customfixes`, its former `Source/CustomFixes/DarklightFire/`) on 2026-11-22.

### Code layout

- `DarklightFireStartup.cs`: `[StaticConstructorOnStartup]` entry point. Swaps vanilla `CompFireOverlay` /
  `CompDarklightOverlay` for `CompFireOverlay_DarklightAware`, turns on the darklight toggle, and hides the
  wood-burning darklight duplicates from the architect menu (`Designator_Build.Visible` postfix).
- `CompFireOverlay_DarklightAware.cs`: the replacement overlay; picks the normal or darklight flame per frame.
- `Forge_DarklightFlames.cs`: Dynamic Effects Forge (`blues.forge`) compat. Everything is looked up by name
  through reflection; Forge is not referenced.

## Build & Development

**Build**: `dotnet build` from `Source/EverythingDarklight/`
**Output DLL**: `1.7/Assemblies/EverythingDarklight.dll`

There are no automated tests — verification is done by loading the mod in-game.
Register debug actions in a `DebugActions_EDL.cs` file; they appear in the dev mode menu.

Always check if something can be done with vanilla mechanics before writing custom C# code. Prefer XML-only solutions when possible.

Performance is important. Cache when it makes sense. Be very careful with everything that executes per tick.

BUILD the DLL at the end of every task to verify there are no compile errors.

## Source Lookup Paths

- RimWorld decompiled source: `d:\Rimworld Modding\Rimworld Source`
- Steam workshop mods: `e:\SteamLibrary\steamapps\workshop\content\294100\`

## XML Conventions

- All mod-specific def names use the `EDL_` prefix (e.g. `EDL_MyThing`).
- Patches go in `Patches/`; use `<mods>` guards for optional mod compatibility.
- Conditional content goes in `Mods/<ModName>/` folders loaded via `LoadFolders.xml`.

## C# Conventions

- Use C# latest features (`LangVersion latest` is set in the .csproj).
- Target framework is `net10.0` — RimWorld 1.7 runs on the Waylith engine with CoreCLR (.NET 10), not Unity/Mono. Unity types (`Rect`, `Color`, `Vector3`, …) come from `UnityStubs.dll`, which the game redirects `UnityEngine*` references to.
- References come from `Krafs.Rimworld.Ref` `1.7.5014-unstable`; bump it to match `Version.txt` in the game folder after game updates.
- Harmony is compiled against `Lib.Harmony` `3.0.0-rc.7` with runtime assets excluded; at runtime the Harmony mod (`brrainz.harmony`) provides it. Keep the version in step with what that mod ships.
- Static def references go in `DefOf.cs` using the `[DefOf]` attribute.
