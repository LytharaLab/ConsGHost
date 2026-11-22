# Security and diagnostic data

## Reporting a vulnerability

Use **Report a vulnerability** on the repository's [Security page](https://github.com/LytharaLab/ConsGHost/security) when private reporting is available. Include the CGH version, Windows version, affected component, expected and observed behavior, and the steps needed to reproduce it. If private reporting is unavailable, ask for a private contact through [GitHub Issues](https://github.com/LytharaLab/ConsGHost/issues) and leave the vulnerability details out of the public issue.

For ordinary compatibility problems, include the Garry's Mod branch and build, affected mod versions, and a short excerpt from `events.jsonl` with the relevant error.

## Local files and permissions

- `cgh_settings.json` stores the game and .NET directories, extra DLL directories, and launch arguments.
- `Sessions/` stores event logs and per-process launch plans. Logs can contain paths and mod-supplied text. Plans contain local file paths and per-session authentication tokens. Keep the plans private.
- `Mods/<mod-id>/config.json` stores settings for each configurable mod. `Data/<mod-id>/` holds files written by the mod. Check both before sharing anything from those directories.

CGH writes its diagnostics locally. Steam, the game, and installed mods have their own behavior. Review excerpts for usernames, paths, tokens, and personal data before attaching them to a report.

In-process mods and extra DLLs run inside the game with the current user's permissions. Separate mod processes provide a crash boundary and retain the same user permissions. Use code you trust, even when `loadAsSubprocess` is enabled.
