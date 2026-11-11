# Implementation Plan: Native launcher
Date: 2026-11-11. Spec: spec.md

## Technical Context
C#/.NET10 LTS; WPF Windows x64; MSAL.NET4.92.0; ProtectedData10.0.15. Stdlib HTTP,
JSON, ZIP, hashing and ECDSA. No Node/Electron/Helios. SDK installed locally outside repo.
Three components: Aea.Core feature modules, Aea.App native UI, Aea.Publisher administration CLI.
Aea.Tests is an executable regression harness with nonzero failure exit, no game/account fixtures
written to real player storage. Production root LocalAppData/AeaLauncher; tests use temp roots.

## Constitution Check
PASS: no old code; UI no webview; MSAL/DPAPI; signed releases; immutable slots; no raw HTTP
logs; no server changes. Approval and live authentication explicitly external, not falsely complete.

## Project Structure
src/Aea.Core: Storage, Releases, Downloads, Authentication, Minecraft.
src/Aea.App: WPF view and controller; resources/brand.json and signed bundled release.
tools/Aea.Publisher: fresh key generation, pack build/export; local publisher key outside repo.
tests/Aea.Tests: fake HTTP transport and temp storage, malicious archives, signatures and args.
scripts: local build/test/publish. docs: setup, architecture, verification and publisher instructions.

## Design
Release envelope carries base64 payload and ECDSA P256 signature (SHA256, fixed P1363 format).
Bundled public key is immutable trust root. Manifest pins Minecraft JSON SHA1 from official
index, Fabric version and SHA512 of mod artifacts. HTTPS host allowlist on download boundary.
Installer verifies every download to temp then atomically moves it; slot receipt is content-hash
bound. Active JSON pointer replaced only after complete preparation; game directory stays stable.
JAR libraries/assets are content-addressed; native files extracted into versioned slot.
Fabric mods use fabric.addMods from slot; managed updates never write personal game files.
MSAL loopback/system browser + PKCE; encrypted cache handles offline refresh. Xbox/MC tokens
memory only. Launch creates ProcessStartInfo.ArgumentList, never shell command text.
UI async with cancellation and exclusive operation gate; single instance per profile.

## Verification
Security regression harness; real metadata/preparation smoke; WPF renders screenshot from its
own RenderTargetBitmap in smoke mode. Self-contained single-exe publish then smoke actual exe.
Live login requires new own Client ID/loopback setup and Minecraft approval; leave unconfigured
until explicitly set, no old app/config copied. No source publication within this task.
