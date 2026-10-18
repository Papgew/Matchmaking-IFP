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
