---
title: Use with AI Agents
description: Install the agent skills that ship inside the Prague NuGet package into your repository, so Claude Code, Copilot and other coding agents write Prague code against the version you reference.
---

# Use with AI Agents

**Goal:** a coding agent in your repository that knows Prague's rules (pooled results are disposed, frozen queries have no row-order guarantee, every write for a key goes to one Kafka partition) and the API of the exact version you reference.

**Prerequisite:** a project that references the `Prague` package.

## What ships

The `Prague` package carries five [agent skills](https://agentskills.io): Markdown files with a short YAML header that coding agents load when a task matches their description.

| Skill | Covers |
|---|---|
| `prague-modeling` | declaring caches, keeping your own copy of a topic's contract, choosing indexes, foreign keys, equality |
| `prague-queries` | eager queries, pooled results, sorting and paging, prepared and frozen queries, joins, the registry |
| `prague-kafka` | `AddKafkaCaches`, topic templates, initial load, hosting, producing, health checks, metrics |
| `prague-handlers` | header, key and value filters, `treatAsDelete`, after-handlers and every `UpdateType` |
| `prague-testing` | broker-free tests, driving handlers by hand, frozen-against-eager checks, Testcontainers |

They sit at `skills/<name>/SKILL.md` inside the package. After `dotnet restore` they are in the NuGet cache, for example `~/.nuget/packages/prague/<version>/skills/prague-queries/SKILL.md`. Every Prague release ships skills written for that release.

Only the `Prague` package carries them. `Prague.Core`, `Prague.Kafka` and the other packages do not, so referencing `Prague.Kafka` next to `Prague` does not give the install tool two copies of a skill (it refuses to install a skill name that two packages share).

## Install into your repository

Agents do not read the NuGet cache. [`dotnet-package-skills`](https://www.nuget.org/packages/dotnet-package-skills) copies the skills of your direct package references into the repository:

```bash
dotnet tool install -g dotnet-package-skills --prerelease
dotnet-package-skills install            # from the folder with your project or solution
```

It writes one folder per skill under `.agents/skills/` (`prague-modeling/SKILL.md`, `prague-queries/SKILL.md`, …) and a `.dotnet-package-skills.json` manifest. Commit them like any other project file. Re-run `install` after you upgrade Prague: the copies do not follow the package version on their own.

`.agents/skills` is the default destination. Claude Code reads `.claude/skills`; install there instead:

```bash
dotnet-package-skills install --destination .claude/skills
```

## Inspect without installing

[`dotnet-inspect`](https://www.nuget.org/packages/dotnet-inspect) lists the skills of a package or of a project's direct references:

```bash
dotnet-inspect package Prague -S Skills
dotnet-inspect project MyApp.csproj -S Skills
```

## Projects without the facade

A project that references `Prague.Core`, `Prague.Attributes` and `Prague.Codegen` directly gets no skills from them. Copy the files from [`skills/`](https://github.com/nanov/prague.net/tree/main/skills) in the repository at the tag of your version.

## Other resources for assistants

- [llms.txt](https://nanov.github.io/prague.net/llms.txt): a short index of every docs page.
- [llms-full.txt](https://nanov.github.io/prague.net/llms-full.txt): the full dense reference, checked against the source.

The skills are hand-written and checked against the source for each release. When a skill and the code disagree, the code is right; please [open an issue](https://github.com/nanov/prague.net/issues).
