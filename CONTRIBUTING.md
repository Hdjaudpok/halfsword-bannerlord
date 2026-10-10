# Contributing

Every update to this project lives in this repository ([`LICENSE`](LICENSE), section 2).

1. Fork the repository on GitHub and make your change on a branch.
2. Open a pull request here with:
   - what you changed;
   - how you tested it (game versions, the lab setup or a playtest);
   - what you measured.
3. Add a dated entry to `MODLOG.md` for anything you learned, especially gotchas.

## Rules
- Never commit game files, extracted assets, decompiled code or values dumped from Half Sword. Tools that read the
  player's own install are welcome; their output stays outside the repository.
- Never add absolute paths from your machine. Use `%USERPROFILE%`, `$PSScriptRoot` or paths relative to the repository.
- No secrets (API keys, tokens, webhooks).
- Single player only: no cheats against other players, and no anti-cheat, DRM or ownership-check bypass.

By opening a pull request, you agree that your contribution is published here under the project's licence.
