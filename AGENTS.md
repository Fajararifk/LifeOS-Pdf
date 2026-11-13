# Codex Instructions — LifeOS.Pdf

This repository is shared with Claude Code. Keep `CLAUDE.md` and everything
under `.claude/` unchanged. They remain the canonical project documentation
and role definitions.

## Read first

Before planning, reviewing, or changing code, read:

1. `CLAUDE.md` — project rules, architecture, non-negotiable constraints, and verification.
2. The relevant role guide in `.claude/agents/`.
3. `docs/PHASES.md` — which phase is active and what it contains.

Do not copy their detailed rules into this file; follow them directly.

## Required delivery workflow

Every feature or material change follows this sequence:

`analyst → architect → developer → qa-reviewer`

- **Analyst**: scope, PDF spec sections involved, requirements, edge cases.
  Save output as `docs/requirement-<fase>.md`.
- **Architect**: public API, internal design, file layout, test strategy.
  Save output as `docs/design-<fase>.md`.
- **Developer**: read the requirement and design documents plus
  `.claude/agents/developer.md`. Start in planning mode, present the required
  implementation plan, and wait for explicit user approval before writing
  code. Execute only the approved plan.
- **QA reviewer**: read-only review after development. Follow
  `.claude/agents/qa-reviewer.md` and save the report as `docs/qa-<fase>.md`.

A phase may proceed only when the preceding phase's QA report is `PASS`.
If a QA report finds blockers, return to the developer planning step; do not
make unplanned fixes.

## Codex execution rules

- Treat `CLAUDE.md` as authoritative for target frameworks, architecture,
  PDF validity, licensing, and verification requirements.
- The core package `src/LifeOS.Pdf` must stay dependency-free and compile on
  every target framework, including netstandard2.0.
- For code changes, run `dotnet build -c Release` and `dotnet test -c Release`;
  before closing a phase, also run the sample and `tools/validate_pdf.py`.
- Keep implementation reports concise: changed files, validation results,
  manual viewer checks, and QA handoff context.
