# Feature Specification: Native launcher

**Created**: 2026-11-11
**Status**: Local implementation verified; external authentication/game acceptance pending
**Input**: New Windows launcher from zero, minimal UI, play/update, automatic dependencies,
Microsoft login, RAM maximum, reusable packs. No old code, config, branding images or publishing.

## User Scenarios & Testing
### US1 - Open and play (P1)
Player opens one executable, signs in and selects Play. Required runtime, game and mods are
downloaded and verified automatically. An authenticated, owned game launches in its own folder.
Acceptance: on a clean profile preparation completes without manual dependency installation;
missing account or app approval never launches a game or reports success.
The game opens its normal title screen. The player chooses Multiplayer and the saved
server; the launcher never automatically joins a server.
### US2 - Receive a pack update (P1)
Opening detects a newer signed pack. Update replaces Play until explicitly selected. Successful
installation returns to Play; a failed/cancelled update preserves the active release and saves.
### US3 - Preferences and diagnosis (P2)
Only RAM maximum is editable. Account can be signed out. Failures show a cause and action;
support logs retain only stages, safe error codes and timestamps.

## Requirements
- FR-001: Windows x64 executable runs without an installer or separately installed launcher runtime.
- FR-002: Minimal keyboard-accessible UI, no artwork, only account, RAM and primary action.
- FR-003: Real Microsoft login using system browser; persistent sessions encrypted for this user.
- FR-004: Xbox/Minecraft auth, active Java entitlement and profile required before launch;
  expired sessions renewed. Missing/unavailable entitlement never enables gameplay.
- FR-005: Install correct Java, official game assets/libraries, Fabric and required mods automatically.
- FR-006: Signed, versioned pack metadata; HTTPS and per-file hashes. Reject invalid signatures,
  unsupported schema, unsafe paths, conflicting files and downgrade attempts.
- FR-007: Explicit, cancellable update; activation atomic after verification; previous release survives failure.
- FR-008: Preserve worlds/options; isolated storage; never inspect other launcher profiles.
- FR-009: RAM maximum validated against machine memory and persisted; no extra player settings.
- FR-010: Logs omit credentials/PII; actionable errors for missing app configuration and API denial.
- FR-011: Pack publisher resolves required dependencies, signs exports and supports future packs.
- FR-012: No paid services, remote publication or server changes. Administrator configuration is
  external to player UX. New app registration/approval and update hosting require separate setup.

## Key Entities
Signed pack release, immutable installation slot, active-release pointer, game instance,
encrypted account cache, player preferences, publisher signing key (never distributed).

## Edge Cases
Network loss, low disk space, corrupt ZIP/hash/signature, zip slip/reparse points, cancellation,
another running launcher/game, broken cache, OAuth cancellation, API403, expired tokens,
unsupported game rules, native-library architecture and .NET single-file extraction permissions.

## Success Criteria
- SC-001: All local security/integrity/rollback tests pass and build has no warnings.
- SC-002: Packaged exe opens the actual minimal UI and operates from a disposable profile.
- SC-003: A fresh, real preparation downloads and verifies the selected game and pack.
- SC-004: Real Microsoft/game launch verified only after app approval, separately from fixtures.
- SC-005: Source/build scripts/docs sufficient to reproduce, export and sign another pack.

## Assumptions
First pack targets current stable Java Edition, Fabric, client performance mods and brightness.
No new server or world is created. External approval is not guaranteed or timed by the launcher.
