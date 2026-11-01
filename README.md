# Matchmaking & Skill Rating

## Team
Altybayev Aldiyar

A small .NET project for calculating player MMR changes after a match and assigning a rank.

## 1. Goal
The application reads a CSV file, validates match records, applies one of two rating policies, and prints a sorted result list.

Input CSV format:
`PlayerId,CurrentMMR,Kills,Deaths,MatchDurationMinutes,IsWin`

Output:
A list of `PlayerMatchResult(PlayerId, OldMmr, MmrDelta, NewMmr, Rank)`, sorted by `NewMmr` descending and then by `PlayerId`.

## 2. Rules
A match is invalid if:
- `MatchDurationMinutes < 3` (exactly 3 is allowed)
- `Kills < 0`
- `Deaths < 0`
- `CurrentMMR < 0`

After a valid match:
- `NewMmr = max(0, CurrentMMR + delta)`

Ranks:
- `< 1000` => Bronze
- `1000–1499` => Silver
- `1500–1999` => Gold
- `2000–2499` => Platinum
- `>= 2500` => Diamond

### Policy A (Win/Loss)
`delta = ±25 + clamp(Kills - Deaths, -3, +3)`

### Policy B (Performance-First)
The match result is ignored.

`KDA = Kills / max(Deaths, 1)`

`delta = round( clamp((KDA - 1) * 20, -40, 60) + min(minutes, 30) / 30 * 10 )`

Rounding is away from zero.

## 3. Run the project (.NET SDK 8.0+)
Run from the project root, where `Matchmaking.sln` is located.

```bash
dotnet build
dotnet run --project src/Matchmaking.App -- data/matches.csv both   # both policies
dotnet run --project src/Matchmaking.App -- data/matches.csv A      # policy A only
dotnet run --project src/Matchmaking.App -- data/invalid.csv both   # validation error, exit code 1
dotnet run --project src/Matchmaking.App -- data/empty.csv both     # empty result
dotnet test
```

## 4. Sample data and expected output (`data/matches.csv`)

| Player | MMR | K/D | Minutes | Win | Note |
|---|---:|---:|---:|---:|---|
| P001 | 1480 | 10/5 | 20 | yes | valid |
| P002 | 1520 | 2/8 | 15 | no | rank differs by policy |
| P003 | 990 | 5/5 | 3 | yes | boundary: exactly 3 minutes is valid |
| P004 | 2100 | 8/2 | 2 | yes | rejected: too short |
| P005 | 1200 | -1/3 | 10 | no | rejected: negative kills |
| P006 | 1990 | 20/0 | 25 | yes | deaths = 0 |

### Policy A expected results
- P006: +28 -> 2018 -> Platinum
- P001: +28 -> 1508 -> Gold
- P002: -28 -> 1492 -> Silver
- P003: +25 -> 1015 -> Silver

### Policy B expected results
- P006: +68 -> 2058 -> Platinum
- P002: -10 -> 1510 -> Gold
- P001: +27 -> 1507 -> Gold
- P003: +1 -> 991 -> Bronze

Important difference:
- order changes (P002 and P001 swap places)
- P002 moves from Silver to Gold
- P003 moves from Silver to Bronze

## 5. Pipeline stages
The project uses three pipeline stages in `src/Matchmaking.Core/Stages.cs`:

| # | Function | Input -> Output | Rule |
|---|---|---|---|
| 1 | `FilterValidMatches` | `IEnumerable<MatchRecord>` -> `ImmutableArray<MatchRecord>` | remove invalid matches |
| 2 | `ApplyMmrPolicy` | `IEnumerable<MatchRecord>`, `Func<MatchRecord,int>` -> `ImmutableArray<RatedMatch>` | apply policy and compute new MMR |
| 3 | `BuildPlayerResults` | `IEnumerable<RatedMatch>` -> `ImmutableArray<PlayerMatchResult>` | create result cards and rank them |

The composition is done by `MatchmakingPipeline.Run` in `Pipeline.cs`:
`Filter -> ApplyPolicy -> BuildResults`

Example trace for P002 under policy A:
`MatchRecord(P002, 1520, 2, 8, 15, false)` -> `RatedMatch(..., -28, 1492)` -> `PlayerMatchResult(P002, 1520, -28, 1492, Silver)`

## 6. Project structure
- `src/Matchmaking.Core/Models.cs` — immutable domain models
- `src/Matchmaking.Core/MatchParser.cs` — CSV parsing and validation
- `src/Matchmaking.Core/Stages.cs` — pipeline stages
- `src/Matchmaking.Core/RatingPolicies.cs` — Policy A and Policy B
- `src/Matchmaking.Core/Pipeline.cs` — pipeline composition and policy selection
- `src/Matchmaking.App/Program.cs` — console entry point and file I/O
- `tests/Matchmaking.Tests/PipelineTests.cs` — xUnit tests

## 7. Requirements covered
- immutable models
- parsing/validation at the boundary
- null is not treated as empty input
- at least 3 clean stages
- higher-order function with delegate call `policy(m)`
- two policies, chosen outside the pipeline
- pure core logic; UI/console I/O only in the app layer
- tests for edge cases and behavior

## 8. Notes
This solution follows a functional pipeline style, where the domain logic stays separate from file and console operations.


## Assignment 6 — Handling Temporal Coupling

**Before (unsafe conceptual example):** `Complete(match)` could calculate a result directly from an unconfirmed match. The caller had to remember to validate and confirm first. Omitting either step could publish an invalid rating.

**After:** `ValidatedMatch.Create(raw)` checks nonempty PlayerId, nonnegative MMR/kills/deaths, and finite duration >= 3 minutes. Its constructor is private; only `ValidationOutcome.Valid` carries a validated value, while `Invalid` carries a reason. `MatchWorkflow.Start(validated)` creates `Pending`; `Confirm(Pending)` produces `Confirmed`; `Complete(Confirmed, policy)` produces `Completed` with the rating. Transitions use switch pattern matching. Attempts to complete Pending, confirm Completed, or repeat confirmation/completion return `TransitionOutcome.Rejected` with the original state unchanged. The app now uses this sequence for each parsed record. Invalid records are skipped with explicit reasons, preserving the Assignment 5 behavior of excluding invalid playable matches. The existing three-stage pipeline remains available for Assignment 5 tests; the new workflow reuses its rating and result stages.

**Demonstration:** `dotnet run --project src/Matchmaking.App -- data/matches.csv both` (from repository root). Run `dotnet test` for Assignment 5 and 6 tests. SDK specified in `global.json`: .NET 8.0.100 with latestFeature roll-forward.

**Tests added:** `WorkflowTests.cs`: factory valid boundary, duration rejection, negative kills, missing player, null record, missing confirmation, valid completion and MMR, repeated confirmation, repeated completion, and handling every state. Expected outcomes are asserted using xUnit. Run locally to verify actual results.


## Assignment 7 — Exception Handling in Functional Programming

Assignment 7 builds on the existing Assignment 5 pipeline and Assignment 6 validated factory and state transitions.

- `Result.cs` defines `Result<T>.Success`, `Result<T>.Failure(DomainError)`, `Map`, `Bind`, and `Option<T>.Some/None`. Failed operations bypass downstream callbacks.
- `RatingWorkflow.cs` uses `Option` to find a player (including players with zero MMR) and `Result` to compose validation, confirmation, and completion. Domain errors include `PLAYER_NOT_FOUND`, `INVALID_MATCH`, and `INVALID_TRANSITION`.
- `RateAll` returns either the complete sorted set of ratings or one failure; it does not publish partial batches.
- `CsvDataLoader.cs` converts missing files, inaccessible files, I/O failures, and malformed CSV into explicit failures.
- `Program.cs` handles the final result at the console boundary and prints actionable error codes.
- `ResultWorkflowTests.cs` adds 12 xUnit tests for successful and failed chains, lookup, Map/Bind, file failures, and batch atomicity.

### Before and after

**Before:** file loading could throw, and workflow rejections were handled separately from pipeline composition.

**After:** expected failures are represented as values, `Map` and `Bind` short-circuit failed computations, and invalid batches produce no partial results. The existing Assignment 5 and 6 code and tests remain.

### Run

```bash
dotnet build Matchmaking.sln
dotnet test Matchmaking.sln
dotnet run --project src/Matchmaking.App -- data/matches.csv A
dotnet run --project src/Matchmaking.App -- data/matches.csv A P001
dotnet run --project src/Matchmaking.App -- data/matches.csv A UNKNOWN
dotnet run --project src/Matchmaking.App -- data/invalid.csv A
dotnet run --project src/Matchmaking.App -- data/not-found.csv A
```

Expected failures include `PLAYER_NOT_FOUND`, `MALFORMED_CSV`, and `FILE_NOT_FOUND`. Build and test execution must be verified in an environment with .NET 8 SDK.


## Assignment 8 — Extension Methods, LINQ, Laziness and Native C

**Maintains Assignments 5–7:** immutable match models, original three-stage pipeline, validated factory, Pending/Confirmed/Completed transitions, Result and Option failures. The new reports operate on the same CSV data.

### Platform and build (supported: Linux x86-64, Windows x86-64)

- .NET SDK 8.0 (see `global.json`); C compiler: GCC (tested locally with Debian GCC 14.2.0 on Linux x86-64); `bash` on Linux or PowerShell and MinGW-w64 GCC on Windows.
- Native source: `native/bonus.c` and `native/bonus.h`. The C export is `int mm_bonus(int kills, int deaths, int weight)`, using the C calling convention. C# binds it via `DllImport("matchbonus", EntryPoint="mm_bonus", CallingConvention=Cdecl)`.
- Inputs: signed 32-bit integers; kills and deaths must be nonnegative, weight 0..10. `NativeBonus.Compute` rejects invalid inputs with `INVALID_NATIVE_INPUT` before calling C. Output: signed 32-bit integer, `clamp((kills-deaths)*weight, -30, 30)`. Internally C uses 64-bit arithmetic to avoid overflow. Example: kills=10, deaths=2, weight=2 yields 16.
- Build Linux: `bash native/build-linux.sh`, producing `native/linux-x64/libmatchbonus.so`. Equivalent exact command: `gcc -std=c11 -O2 -Wall -Wextra -fPIC -shared native/bonus.c -o native/linux-x64/libmatchbonus.so` (create the output directory first).
- Build Windows: `powershell -ExecutionPolicy Bypass -File native/build-windows.ps1`, producing `native/win-x64/matchbonus.dll` (requires x64 MinGW-w64 GCC on PATH).
- `Directory.Build.targets` runs the appropriate build script before app/test builds and copies the library beside each output assembly. Run from the **repository root**:

```bash
dotnet build Matchmaking.sln
dotnet test Matchmaking.sln
dotnet run --project src/Matchmaking.App -- data/matches.csv reports
dotnet run --project src/Matchmaking.App -- data/matches.csv reports player-report.csv
dotnet run --project src/Matchmaking.App -- data/matches.csv A
dotnet run --project src/Matchmaking.App -- data/invalid.csv reports
dotnet run --project src/Matchmaking.App -- data/missing.csv reports
```

The optional third argument in `reports` mode is the export CSV filename. The app prints **both reports** and exports the player comparison when requested. `NativeBonus.Compute` is used by `ReportWorkflow.Build` on every valid input record. Missing native binaries and incorrect exports are converted into `NATIVE_LIBRARY_MISSING` / `NATIVE_ENTRY_MISSING` Result failures; wrong architecture is `NATIVE_ARCHITECTURE_MISMATCH`. A common integration difficulty is the runtime not finding the shared library; the MSBuild copy target places it beside the application/test assembly, avoiding a machine-specific global search path. The library must be built for the process architecture.

### Reports and extensions

**Report 1: Player comparison.** Input: actual CSV matches validated and rated using Policy A, with a native bonus for each match. Intended user: player comparing outcomes. Filter: `NewMmr >= minimumMmr` (the app uses 0). Output: PlayerId, NewMmr, Rank, NativeBonus, Score25, Score40. LINQ stages: `Where`, `Select`, `OrderByDescending`, `ThenBy`. Sort: MMR descending, then PlayerId ascending. Expected example: a winning match with kills=5, deaths=2 gets Score25=28 and Score40=43; native bonus=6.

**Report 2: Rank distribution.** Input: the same completed entries. Intended user: matchmaking administrator. Filter: accepted valid matches. Output: Rank, Players, TotalNewMmr, TotalNativeBonus. LINQ stages: `GroupBy`, `Select`, `Count`, `Sum`, `OrderByDescending`, `ThenBy`. Sort: total MMR descending, then rank enum ascending. Example: two Gold players with MMR 1500 and 1400, bonuses 6 and 4, yield Gold/2/2900/10.

**Extension 1:** `WithMinimumMmr(this IEnumerable<ReportEntry>, int)` returns a deferred `IEnumerable<ReportEntry>`. It belongs on a queryable sequence because it expresses a reusable, composable domain filter.

**Extension 2:** `ToRankTotals(this IEnumerable<ReportEntry>)` returns `ImmutableArray<RankTotal>`. It belongs on the sequence because it summarises that collection into rank totals. Both extensions are invoked by report methods used in the console app.

**Currying:** `CreateScore(int winWeight)` returns `Func<MatchRecord,int>`. Partial application `CreateScore(25)` and `CreateScore(40)` creates two independent functions used on the same input in Report 1, without mutable global settings. On a winning match with kills=5, deaths=2 the outputs are 28 and 43.

### Deferred execution and resource ownership

`MatchReports.StreamRankedEntries` uses `yield return` to defer filtering until enumeration. Merely constructing the enumerable does not process records. The app calls `ToArray()` exactly once after constructing the stream; both reports then reuse that snapshot. Before materialisation, enumerating the deferred sequence again repeats the filter and traverses its source; afterwards, enumerating the array does not rerun the query. The xUnit counter probe verifies this timing without introducing side effects into production logic.

`CsvDataLoader.Load` uses `File.ReadAllLines`, a convenience API that internally opens and closes its file handle, returning an in-memory snapshot. This avoids yielding values from a disposed reader. Report export uses `using var StreamWriter` in the console layer; the app owns and disposes it on success and exceptions. The tests verify file handles can be reopened exclusively after both normal and exceptional disposal. A failed batch returns a Result failure without exposing partially constructed report rows.

**Retention and allocation:** `ReportWorkflow.Build` materialises the input once into an immutable array and builds a result snapshot; `StreamRankedEntries` can retain a reference to its source until enumeration completes, so the app materialises once and releases the iterator. Report arrays remain in scope only during display/export. No persistent caches, event subscriptions or long-lived collections are used. Streaming can reduce temporary memory, while snapshots make repeated report queries deterministic and prevent repeated input reads. The native function uses scalar arguments and allocates no native memory; there is no native handle to release.

### Assignment 8 test coverage and current status

`tests/Matchmaking.Tests/Assignment8Tests.cs` adds **18 xUnit tests**: both extension methods; grouping and totals; deterministic tie-breaking; empty and no-match reports; two configured functions; lazy timing and snapshot reuse; actual native calculation and clamp; invalid native input; simulated missing native library; successful full workflow; failed workflow without partial results; invalid match blocking native invocation; StreamWriter disposal on success and exception; missing and malformed file boundaries. Previous Assignment 5–7 tests remain.

**Latest verification:** the C library compiled with GCC 14.2.0 on Linux x86-64 and a real `ctypes` call returned the manually expected 16. The .NET SDK is not installed in the authoring environment, so `dotnet build` and `dotnet test` have **not** been executed here; run them locally and record the results before submission. Generated `bin/`, `obj/`, `native/linux-x64/` and `native/win-x64/` binaries are reproducible outputs; include the sources and build scripts in a submitted ZIP.

**Contribution:** Altybayev Aldiyar — project owner. Assignment 8 implementation and test files are listed above; verify and be prepared to explain each part during the individual defense.
