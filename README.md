# Aea Launcher

An independent Windows x64 launcher for a small Minecraft Java Edition community.
Written in C# and WPF, with a minimal interface: Microsoft account, maximum RAM,
and a Play button that becomes Update when a new modpack release is available.

This is an unofficial project, not affiliated with or approved by Mojang or Microsoft.
Minecraft is a trademark of Mojang and Microsoft.

## What it does

- Prepares an isolated game directory without importing another launcher's profiles.
- Downloads the required Java runtime, official game assets and libraries, Fabric,
  performance mods and a brightness mod from their original providers.
- Verifies artifact hashes and signed modpack metadata before accepting files.
- Installs a pack update only after the player selects Update. Failed updates preserve
  the previously active release and do not replace worlds or player preferences.

The executable does not contain or redistribute a copy of Minecraft. Game files are
downloaded separately from official providers. This repository is currently the public
project information page; it is not a release of the launcher or the game.

## Authentication and license checks

Microsoft sign-in uses MSAL.NET, the system browser, authorization code flow with PKCE,
and a loopback redirect. Passwords are entered on Microsoft's page, not collected by
an account form operated by this project.

Before every game launch, the application obtains Xbox Live/XSTS and Minecraft session
authorization, requests an active `game_minecraft` entitlement and validates the Java
profile. Missing licenses, unavailable license verification or missing profiles block
gameplay. The application offers no offline accounts, demo bypass or disabled license checks.

## Local data and updates

The Microsoft token cache is encrypted with Windows DPAPI for the current user. Xbox and
Minecraft session tokens remain in memory. The launcher does not operate a separate
account collection or analytics server. Its diagnostic file records stages and error
codes, excluding passwords, tokens, email addresses and full launch arguments.

Pack releases are signed with ECDSA P-256/SHA-256. Only the public verification key is
distributed; the publisher's private key stays outside the launcher. This verifies the
origin of pack metadata, not the safety of arbitrary third-party mods.

## Current status

Developer preview. The application identity is registered, but access to Minecraft
services has not yet been approved. Real authenticated gameplay is not yet validated.

Local regression checks cover authentication failures, artifact integrity, update recovery,
player data preservation and the packaged UI, alongside real Java/game file preparation.
Authentication and update tests use simulated responses;
Java `--dry-run` checks loading without executing the game. A hosted update release and
end-to-end authenticated play remain pending.

No public binary or game download is currently offered here. Application access remains
subject to Microsoft's review and the Minecraft EULA and Usage Guidelines.

## References

- [Minecraft EULA](https://www.minecraft.net/en-us/eula)
- [Minecraft Usage Guidelines](https://www.minecraft.net/en-us/usage-guidelines)
- [MSAL.NET system-browser authentication](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers)
