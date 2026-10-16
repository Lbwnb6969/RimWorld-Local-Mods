# Recovered EES Core

This is a source recovery of wowgag's locally installed Evolving Enemy Strongholds, for this private modpack integration. It is not an author-provided source release. No redistribution license has been verified.

- Original Workshop item: https://steamcommunity.com/sharedfiles/filedetails/?id=3738072776
- Input DLL SHA-256: `1F22DBEB2133A99F92D1B2B11F1948AB3F5E345A8CEB989A27019FD0C720821D`
- Recovery tool: ILSpyCmd 9.1.0.7988 with the installed RimWorld managed assemblies and Harmony references.
- Unmodified recovered project and original assets: `../Deprecated/Backups/20261001-ees-round0/`.
- Input/resource manifest: `../Docs/EES_CORE_INPUTS.manifest.json`.

The initial recovery contains 180 C# files. Game logic has not been intentionally changed in round 0. The project reference paths were corrected, dependency copying was disabled, deterministic builds enabled, and C# 10 selected to match the original authored type surface without adding newer compiler support types. Generated trailing whitespace was trimmed in 16 source files; the unmodified recovery remains archived.

Build from the Workshop workspace root:

```powershell
dotnet build EvolvingEnemyStrongholds-MedievalPatch/CoreSource/EvolvingEnemyStrongholds.csproj -c Release
```

The output stays in `bin/Release/net48`. It is not deployed automatically. `RimWorldManagedDir` and `HarmonyAssemblyPath` can be overridden as MSBuild properties for another installation.

Round 1 adds `LocalSettingsMigration` (181 C# files total). Build and assemble the source-built core, adapter, and pinned resources with `../Tools/Build-StandaloneEES.ps1`. The package loads `Standalone` only when Workshop EES is inactive. The existing adapter therefore remains usable with the original core until the final configuration switch. Runtime and migration results are recorded in `../Docs/EES_STANDALONE_ROUND1.zh-CN.md`.

The round 2 foundation adds a versioned generation plan and an NPC turret interaction patch (183 C# files total). It persists previously transient map/command coordinates, observes legacy maps without rebuilding them, and fixes temporary war penalties that did not restore tier. `../Docs/EES_PERSISTENT_PLANNING_ROUND2.zh-CN.md` records scope and runtime evidence. The new large center layouts remain unfinished.

The contract audit compares authored type/member identities, field signatures, method/parameter signatures and defaults, relevant attributes, assembly references, and string literals in save methods directly against the original PE metadata. Compiler closure types and iterator suffixes are excluded because they are compiler implementation details. A passing audit does not establish method-body equivalence or successful gameplay; isolated runtime checks remain required.

Keep the original namespace, assembly name/version, Def names and save keys during initial migration. Never load both core assemblies; the conditional load folder enforces this while both mod entries are active. Preserve the original Korean strings and source encoding unless a separately validated localization change requires editing them.
