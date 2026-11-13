# Matchmaking & Skill Rating — Final Project

**Course:** Introduction to Functional Programming (2026–2027)  
**Student / project owner:** Altybayev Aldiyar  
**Student group:** 2513  
**Final submission branch:** `final-project`  
**Final commit:** use `git rev-parse HEAD` on the submitted branch; the exact SHA is also included in the submitted group report.  
**Platform:** .NET 8.0 SDK and C compiler for the native component.  
**Verified CI code commit:** `ba64487549c232d7cbde3b27a11815acadab2a21` ([GitHub Actions run](https://github.com/Papgew/Matchmaking-IFP/actions/runs/43518995851)).

## Overview

A console program evaluates match performance, updates player MMR using configurable scoring policies, recommends compatible matchmaking pairs, and produces two analytical reports using real native C calculations. The main domain is one match record per player per input batch. The program has **three distinct workflows**:

1. **Rating & ranking** — Parse CSV, filter unplayable match records, apply Policy A/B through a three-stage pure pipeline, and print the ordered leaderboard. A specific player can be looked up with an explicit `Option` failure path.
2. **Pair recommendation** — Calculate updated MMR, sort eligible players and greedily pair neighboring players with a configurable maximum MMR gap; list unmatched players. Reject duplicate IDs and invalid gaps.
3. **Analytics & export** — Validate/rate matches, compute native C performance bonuses, produce a filtered player comparison report plus a grouped rank distribution report, and optionally save CSV.

`src/Matchmaking.Core/Models.cs` contains immutable match and rating records; `PairingPlanner.cs` and `MatchReports.cs` define additional domain outputs. Application I/O lives in `src/Matchmaking.App/Program.cs` and file access is confined to `CsvDataLoader.cs`/the console export boundary. There are no databases, credentials, external APIs, or mutable global settings.

## Requirements and rules

Input CSV columns: `PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin`.

- Required `PlayerId`; integer nonnegative current MMR, kills and deaths; finite duration **at least 3 minutes**; Boolean win flag. Zero and `false` are legitimate values.
- Malformed field counts/types reject the entire CSV file with `MALFORMED_CSV`. Missing files yield `FILE_NOT_FOUND`; empty CSV is valid. Parsed but non-playable match rows are **skipped with an explicit reason** in bulk workflows.
- Final MMR: `clamp(CurrentMMR + delta, 0, int.MaxValue)`; ranking thresholds: Bronze <1000, Silver 1000–1499, Gold 1500–1999, Platinum 2000–2499, Diamond >=2500.
- Policy A (Win/Loss): `delta = (win ? +25 : -25) + clamp(kills-deaths,-3,3)`.
- Policy B (Performance): `KDA = kills/max(deaths,1)`; `delta = roundAwayFromZero(clamp((KDA-1)*20,-40,60)+min(minutes,30)/30*10)`.
- Pairing: `maxMmrGap` must be >=0. After sorting by final MMR ascending then PlayerId, consider neighbors from left to right and create one pair when the gap is within the limit. Otherwise the earlier player waits. A PlayerId cannot appear twice in one pairing pool.
- Native C scoring: `clamp((kills-deaths)*weight,-30,30)`, weight is 0–10. The reports call the **real C** `mm_bonus` with weight 2 via P/Invoke.

## Environment and exact commands

Supported build systems: Linux x86-64 with Bash and GCC; Windows x86-64 with PowerShell and GCC/MinGW-w64 or MSVC `cl.exe` in Visual Studio Developer PowerShell. **Use the repository root** (the directory with `Matchmaking.sln`) as the working directory.

```bash
dotnet --version
dotnet build Matchmaking.sln
dotnet test Matchmaking.sln

# Workflow 1: rating and player lookup
dotnet run --project src/Matchmaking.App -- data/matches.csv both
dotnet run --project src/Matchmaking.App -- data/matches.csv A P001

# Workflow 2: pair suggestions (A/B, gap 100)
dotnet run --project src/Matchmaking.App -- data/matches.csv pairs A 100

# Workflow 3: both reports, including a real native C call and optional export
dotnet run --project src/Matchmaking.App -- data/matches.csv reports
dotnet run --project src/Matchmaking.App -- data/matches.csv reports player-report.csv

# Failure/edge demonstrations
dotnet run --project src/Matchmaking.App -- data/empty.csv both
dotnet run --project src/Matchmaking.App -- data/invalid.csv both
dotnet run --project src/Matchmaking.App -- data/missing.csv both
dotnet run --project src/Matchmaking.App -- data/matches.csv A UNKNOWN
```

**Native build:** `Directory.Build.targets` compiles `native/bonus.c` before application/test builds and copies the resulting shared library beside the executable/test DLL. Independently: `bash native/build-linux.sh` -> `native/linux-x64/libmatchbonus.so`, or `powershell -ExecutionPolicy Bypass -File native/build-windows.ps1` -> `native/win-x64/matchbonus.dll`. C# `[DllImport("matchbonus", EntryPoint="mm_bonus", CallingConvention=Cdecl)]` passes three signed 32-bit integers and receives one signed 32-bit integer; C uses 64-bit intermediates to avoid overflow. No unmanaged memory ownership or pointer release is required. Missing DLL, missing symbol, and architecture mismatches are reported as Result failures.

If the application builds but `NATIVE_LIBRARY_MISSING` occurs, confirm the native file exists **beside** `Matchmaking.App.dll` in the output directory and has the same architecture as the process. On Windows, open the x64 Developer PowerShell or install an x64 GCC toolchain. Generated native binaries and `bin/`/`obj/` are excluded from the archive and reproduced by the build commands.

## Sample results (calculated expectations; verify by running)

`data/matches.csv` contains six rows, of which P004 (duration 2 minutes) and P005 (negative kills) are skipped in bulk workflows; four rows are accepted.

**Policy A:** P006 2018 Platinum (+28), P001 1508 Gold (+28), P002 1492 Silver (-28), P003 1015 Silver (+25).  
**Policy B:** P006 2058 Platinum (+68), P002 1510 Gold (-10), P001 1507 Gold (+27), P003 991 Bronze (+1).

**Pairing with Policy A and gap 100:** `P002 (1492) + P001 (1508), gap 16`. P003 (1015) and P006 (2018) remain waiting.

**Player comparison under Policy A:** Native bonuses are P006 +30, P001 +10, P002 -12, P003 0. Two separately configured curried functions (`CreateScore(25)` and `(40)`) provide different scores on the same match: P001 -> 28 and 43. Rows sort by new MMR descending and PlayerId ascending.

**Rank distribution:** Silver: 2 players, total MMR 2507, total native bonus -12; Gold: 1 player, MMR 1508, bonus +10; Platinum: 1 player, MMR 2018, bonus +30. Rows sort by total MMR descending, then rank.

## Architecture and evidence map

| Requirement | Implementation / integration |
|---|---|
| Pure immutable models, pattern decisions | `Models.cs` records; `MatchWorkflow.cs` state pattern matching; `PairingPlanner.cs` results |
| Three named pipeline stages | `Stages.FilterValidMatches` → `Stages.ApplyMmrPolicy` → `Stages.BuildPlayerResults`, composed by `MatchmakingPipeline.Run` and used in `Program.cs` |
| Higher-order configurable rules | `MatchmakingPipeline.Run(..., Func<MatchRecord,int>)`, invoked for Policies A/B; `RatingPolicies.cs` |
| Safe temporal ordering | `ValidatedMatch.Create` and `MatchWorkflow` (Pending → Confirmed → Completed); invalid transitions rejected |
| Functional failures; Map/Bind | `Result.cs`, `RatingWorkflow.RatePlayer`; `INVALID_MATCH`, `INVALID_TRANSITION`, `PLAYER_NOT_FOUND`, `DUPLICATE_PLAYER`, `INVALID_PAIR_GAP` |
| Missing lookup | `RatingWorkflow.FindPlayer` returns `Option.Some/None` |
| Two extensions, two LINQ reports | `MatchReports.cs` (`WithMinimumMmr`, `ToRankTotals`, `PlayerComparison`, `RankDistribution`) |
| Currying and partial application | `MatchReports.CreateScore(25)` and `CreateScore(40)` used by console reports |
| Lazy query and materialisation | `MatchReports.StreamRankedEntries` (`yield return`); one `ToArray()` snapshot in console before both reports |
| Persistence and boundary errors | `CsvDataLoader.Load` (CSV), console export with `using StreamWriter`; `data/*.csv` |
| Native C interop | `native/bonus.c`, `native/bonus.h`, build scripts, `NativeBonus.Compute` and report workflow |
| Automated tests | `PipelineTests.cs`, `WorkflowTests.cs`, `ResultWorkflowTests.cs`, `Assignment8Tests.cs`, `PairingTests.cs` |

## Test coverage and verification status

The xUnit suite contains pure pipeline/rank/validation tests, safe sequence and failure-propagation tests, normal/error workflow tests, LINQ extension/aggregation and lazy-evaluation tests, **real P/Invoke** tests, paired-player cases, missing/malformed file tests, and writer disposal tests for success/failure. Run `dotnet test Matchmaking.sln` from a clean extraction; its output is the authoritative pass/fail summary.

**Verification:** GitHub Actions built the full .NET 8 solution successfully with **0 errors**, executed **66 tests successfully**, and successfully ran the rating, pairing and native-report commands on Ubuntu Linux. See [run #43518995851](https://github.com/Papgew/Matchmaking-IFP/actions/runs/43518995851). GCC locally also compiled `native/bonus.c`: independent native calls returned **16**, **30**, and **-30** for the expected cases. A local Windows x64 build still needs checking on the student’s machine.

## Submission and responsibility

- Project owner listed in the repository: **Altybayev Aldiyar**, student group **[fill in]**. No other contributor is asserted.
- Individual work to verify with the owner before defense: pipeline/rules (`Stages.cs`, `RatingPolicies.cs`), state/result workflows (`MatchWorkflow.cs`, `RatingWorkflow.cs`), reports/native interop (`MatchReports.cs`, `NativeBonus.cs`, `native/*`), matchmaking pairs (`PairingPlanner.cs`), tests, and documentation. Provide accurate ownership after checking the commit history and any external assistance.
- Include this Git-tracked source snapshot, sample CSV and test project in the Moodle code ZIP **without `bin/`, `obj/`, native build outputs, or secrets**. Upload the **group report** and the **individual contribution report** separately as DOCX/PDF. The reports are distributed alongside the ZIP; their content should reference the actual code commit shown by `git rev-parse HEAD`.
- During defense: demonstrate rating both policies, pairing with a boundary gap, report export with real C function, malformed-file failure, state rejection, and the test suite; prepare to modify one threshold and corresponding test.
- AI assistance may have been used in implementation/document preparation. Follow the course syllabus: **obtain instructor permission and acknowledge AI use** if applicable; the student must personally understand, verify and defend every submitted component.

### Known limitations

This is a deterministic batch CLI, not a persistent player-account database. Each CSV row represents one player's match in the current batch; pairing requires unique PlayerIds. The pairing algorithm is greedy, not globally optimal, and reports native bonuses rather than automatically adding them to MMR. Native build requires a compatible C compiler. Ubuntu CI compilation, all 66 tests and all workflows passed; Windows native build still needs verification in its own environment.
