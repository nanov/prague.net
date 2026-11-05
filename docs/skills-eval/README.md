# Skills eval

An A/B check that the agent skills in [`skills/`](../../skills/) change what a coding agent writes against Prague. It is a manual harness, not a CI job: rerun it after a skill rewrite or a release that changes the public surface.

## Method

Three tasks, each run by two arms on each target model (Sonnet and Opus):

- **with skills**: the five `SKILL.md` descriptions are in the prompt, and the agent may read the skill files.
- **without skills**: no skill files, but the same public material any user has: the package XML docs and DLLs, `llms.txt`, `llms-full.txt` and the docs site.

| Task | What it exercises |
|---|---|
| [T1](tasks/t1.md) | owning a copied contract, Unique/Many/Range index choice, a Range over a nullable timestamp, stable paging |
| [T2](tasks/t2.md) | a 10k/s endpoint: frozen query built once, total comparer, join, low allocation |
| [T3](tasks/t3.md) | Kafka wiring: header filter, `treatAsDelete`, a projection that includes the initial load, a job gated on the load |

Each run gets an isolated workspace: a `Service.csproj` that references locally packed nupkgs (`dotnet pack Prague.Publish.slnf -o <dir> -p:Version=<v>`, all packages at one version), NUnit and `Microsoft.Extensions.Hosting`. The run rules are in [`tasks/common.md`](tasks/common.md); fill its `__PLACEHOLDERS__` and replace `__SKILLS_RULE__` per arm. The agent must not read the library repo.

Grading:

1. **Objective:** `dotnet build --no-incremental` and `dotnet test` in every workspace.
2. **Blind rubric:** workspaces are copied to neutral names (`t1-W` … `t3-Z`) with arm and model stripped. One grader per task scores all four copies against [`tasks/rubric.md`](tasks/rubric.md). The rubric was written before any output was read: each item scores 0/1/2 and must quote the `file:line` behind it.
3. **Efficiency:** tokens, tool calls and wall time per run, from the agent's usage report.

## Results (round 2, Prague 0.6.0 skills build)

All 12 workspaces built with 0 warnings and passed their own tests.

| Model | Arm | Rubric (of 46) | Tokens | Tool calls | Time |
|---|---|---|---|---|---|
| Sonnet | without skills | 40 | 244k | 36 | 233 s |
| Sonnet | with skills | **46** | 225k | 27 | 189 s |
| Opus | without skills | 46 | 266k | 59 | 497 s |
| Opus | with skills | 46 | 233k | 41 | 342 s |

Sonnet without skills lost points on two real bugs:
- registering the T3 projection with both `AddSingleton<T>()` and `WithAfterHandler<T>()`, so request threads read an instance that never received a message;
- leaving the T1 sentinel lower bound unclamped, so `CountDelivered(long.MinValue, …)` counted undelivered shipments.

Opus reached the same score either way. The skills cut its tool calls by 30% and its time by 31%.

The graders also caught a library bug: the generated `[DataCacheSort]` comparer used the culture-sensitive `Comparer<string>.Default`. Generated comparers now compare strings with `string.CompareOrdinal`.

## Limits

- One run per cell, so the efficiency numbers are single samples.
- Arm isolation is by instruction. The agents' reports say they followed it; nothing enforced it.
- The rubric checks the failure modes the skills target. It can show that the skills fixed those failures, not that they help in general.
