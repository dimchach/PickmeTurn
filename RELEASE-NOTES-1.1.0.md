# PickmeTurn 1.1.0

## Changes

- Updated the bundled FreeTurn Windows core from 4.0.0 to 4.0.1.
- Removed the forced `-manual-captcha` mode.
- Added explicit `-platform desktop`.
- Automatic CAPTCHA solving is attempted first; the FreeTurn core can fall back to a browser when manual confirmation is required.
- PickmeTurn does not treat a later CAPTCHA message as a disconnect condition while the FreeTurn relay session remains alive.
- Added lightweight FreeTurn process monitoring: if the FreeTurn process itself exits while the tunnel is active, PickmeTurn reports the relay failure and performs cleanup.
- Removed development-only C# warnings from the session monitor and unused state.
- Existing UI, profile storage, DPAPI protection, WireGuard integration, tray behavior, and deterministic cleanup are retained.

## Verification

- FreeTurn core 4.0.1 SHA-256: `9669d6babdd7f5eee01aa758605d96a706179d4b94e5baf4d8c70966aab7a801`
- Release candidate was built successfully with no C# compiler warnings.
- Installer was compiled successfully with Inno Setup 6.7.3.
- Upgrade installation over PickmeTurn 1.0.0 was verified: the existing profile remained available and the application connected successfully.
- Automatic CAPTCHA solving was verified during a sustained connection test.

## Notes

The upstream FreeTurn core owns CAPTCHA solving and relay/session recovery. PickmeTurn does not implement a local CAPTCHA solver or suppress provider failures.
