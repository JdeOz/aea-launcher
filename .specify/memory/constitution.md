<!-- Sync impact: initial 1.0.0. Principles: simplicity, boundaries, security,
verification, truthful delivery. Generic spec/plan/tasks templates remain compatible. -->
# Aea Launcher Constitution

## Core Principles
### I. Minimal product, deep modules
Implement only the accepted Windows launcher: play, explicit pack updates, account and RAM.
Each module owns a coherent responsibility; UI delegates network, auth and installation work.
### II. Security by construction
Use Microsoft MSAL and platform cryptography. Encrypt persisted token cache for CurrentUser.
Verify signed release manifests and file hashes. Validate paths, archive entries and URLs at
boundaries. Never execute remote UI, invent crypto or log secrets. No authentication bypass.
### III. Transactional installation
Downloads use temporary files and hash verification; immutable release slots are activated only
after preparation succeeds. Never overwrite saves, options or another launcher's files.
### IV. Verifiable behavior
Test invalid signatures, interrupted updates, path escapes, authentication failures and secrets
redaction. Real login/game claims require real evidence, never fixtures or cached assumptions.
### V. Maintainable delivery
Pin dependencies and locks; use a supported LTS SDK. Keep actionable diagnostics without PII.
Discard replaced implementations. One current codebase, no parallel legacy launcher.

## Constraints
No paid resources, server changes, publishing, logos or illustrations. Application registration
and Minecraft approval remain external requirements; missing configuration fails clearly.

## Development Workflow
Specification, plan, tasks and consistency analysis precede implementation. Routine decisions
are authorized by the user's request to complete the new project. Failures must be diagnosed.

## Governance
Changes update the current intention, include impact review and relevant checks. Do not mark
external acceptance complete while Microsoft approval or authenticated gameplay is unverified.

**Version**: 1.0.0 | **Ratified**: 2026-11-11 | **Last Amended**: 2026-11-11
