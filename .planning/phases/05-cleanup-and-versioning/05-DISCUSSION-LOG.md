# Phase 5: Cleanup and Versioning - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-05-03
**Phase:** 5-Cleanup and Versioning
**Areas discussed:** DatabaseTransactionLeasingScope fate, XML doc build enforcement

---

## Area Selection

| Area | Selected |
|------|----------|
| DatabaseTransactionLeasingScope fate | ✓ |
| Version number | — (user deferred: no adopters, no bump needed) |
| XML doc build enforcement | ✓ |

**User's note:** "Don't worry about major bumping this version. It has no adopters so we can break changes without bumping."

---

## DatabaseTransactionLeasingScope fate

### Q1: How should DatabaseTransactionLeasingScope be kept?

| Option | Description | Selected |
|--------|-------------|----------|
| Make internal sealed (Recommended) | Change access modifier to internal sealed — zero logic change, lowest risk | ✓ |
| Inline into ExecuteInLeaseAsync | Move StartTransaction + DisposeAsync logic into the two overloads — eliminates class, duplicates ~20 lines | |

**User's choice:** Make internal sealed

---

### Q2: What should happen to DatabaseTransactionLeasingScopeFactory?

| Option | Description | Selected |
|--------|-------------|----------|
| Delete it (Recommended) | Dead code — never registered in DI, no callers | ✓ |
| Make internal sealed | Hide from public surface but keep the code | |

**User's choice:** Delete it

---

## XML Doc Build Enforcement

### Q1: Add GenerateDocumentationFile to both csproj files?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, add to both csproj files (Recommended) | Enforces CS1591 as build error via TreatWarningsAsErrors | ✓ |
| Manual audit only | No flag, manual scan only — no build enforcement | |

**User's choice:** Yes, add to both csproj files

---

### Q2: Scope of doc-writing work?

| Option | Description | Selected |
|--------|-------------|----------|
| Fix all missing docs across public API (Recommended) | Single pass over all public types in both packages | ✓ |
| New/changed members only | Only milestone-touched members; suppress CS1591 elsewhere | |

**User's choice:** Fix all missing docs across public API

---

## Claude's Discretion

- Exact wording of XML doc summaries, param descriptions, and returns
- Whether to add `<NoWarn>` entries for obj/ artifacts if they surface unexpected CS1591 warnings

## Deferred Ideas

- **Major version bump** — user confirmed no adopters; revisit when a public release is planned

