# PickmeTurn 1.1.0

## Changes

- Updated the bundled FreeTurn Windows core from 4.0.0 to 4.0.1.
- Removed the forced -manual-captcha mode.
- Added explicit -platform desktop for FreeTurn authentication.
- Automatic CAPTCHA solving is attempted first; FreeTurn can fall back to browser-based manual confirmation when required.
- PickmeTurn no longer treats a later CAPTCHA notification as an immediate disconnect condition while the FreeTurn relay process remains alive.
- Added a lightweight FreeTurn process monitor. If the FreeTurn process exits while the tunnel is active, PickmeTurn reports the relay failure and performs cleanup.
- Existing UI, profile storage, DPAPI protection, WireGuard integration, tray behavior, and connection cleanup are retained.
- The build script pins FreeTurn 4.0.1 and verifies its SHA-256 against the upstream release checksum before embedding it into the application.

## Validation

- Upgraded from PickmeTurn 1.0.0 to 1.1.0 without losing the existing profile.
- Confirmed the installed 1.1.0 client starts normally.
- Confirmed automatic CAPTCHA solving works during connection.
- Confirmed the tunnel remains connected and provides Internet access during extended testing.
- Confirmed the 1.1.0 installer and application binaries are Authenticode-signed and successfully verified with zero warnings and zero errors.

## Security

The bundled FreeTurn Windows core is downloaded from the upstream GitHub release and verified against its published SHA-256 checksum before being included in the build.

The upstream FreeTurn core remains responsible for CAPTCHA handling and relay/session recovery. PickmeTurn does not implement its own CAPTCHA solver.
