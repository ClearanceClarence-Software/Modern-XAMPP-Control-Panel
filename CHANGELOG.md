# Changelog

All notable changes to Modern XAMPP Control Panel. The release script copies the section for each version into
its GitHub release.

## [0.2.1] - 2026-10-28

### Added

- **What's new window.** The update link in the header opens the release notes of every version since the one
  you have, newest first, with a button to the download page.
- `SECURITY.md`: how to report a vulnerability privately, and what counts.

### Changed

- Releases are code-signed with a Certum certificate. They are built, signed and uploaded as drafts on the
  maintainer's PC (`tools/release.ps1`); GitHub Actions now only checks that each push builds.
- The README covers signing, security reports and the update notice, and has a new social preview image.

## [0.2.0] - 2026-10-28

### Added

- **Sites window.** Gives each project its own address, such as `https://myproject.test`. Apply writes the
  virtual hosts to a marked block in `httpd-vhosts.conf`, tests the config with `httpd -t` (and puts every file
  back if Apache rejects it), adds the names to the Windows hosts file, and restarts Apache.
- **Trusted HTTPS for local sites.** A certificate authority that exists only on your computer issues a
  certificate for localhost and every site, and Windows is asked to trust it, so Chrome and Edge show the
  padlock.
- **PHP window.** Edits the common `php.ini` values (memory limit, upload and post size, time limits, input
  vars, display errors, time zone) and turns extensions on and off. The previous file is kept as `php.ini.bak`.
- **FileZilla Server upgrade.** The Versions window runs the official FileZilla Server 1.x installer you
  downloaded, replacing XAMPP's FileZilla 0.9.41 from 2012. The FileZilla row then starts, stops and
  administers the new server.
- **Light and dark theme.** Follows Windows by default; choose Light or Dark in Config, or use the sun/moon
  button in the header.
- **Tray icon.** Shows which modules run and starts or stops them from its menu. Minimizing hides the panel to
  the tray (can be turned off in Config).
- **Update notice.** Checks GitHub for a newer release on startup and shows a link in the header.
- **Config check before Apache starts.** `httpd -t` runs first, so a broken configuration is named in the log
  instead of Apache stopping right after it starts.

### Changed

- The Config window is more compact, with the options in two columns, and scrolls on short screens.
- The side buttons are evenly spaced and the module rows share the card's height.
- The Versions window is now called "Versions".
- Code follows the project's coding standards (`CODING_STANDARDS.md`) throughout.

### Fixed

- Autostart no longer tries to start modules that aren't installed.
- Versions written with commas (FileZilla Server's `1, 12, 6, 0`) are read correctly.
- Edits to `httpd.conf` and `php.ini` keep files saved in the Windows code page intact.

## [0.1.0] - 2026-10-27

First release: the XAMPP-style panel with clean MySQL/MariaDB shutdown, explained startup failures, a port
conflict finder, Netstat, XAMPP-style configuration, and installing, switching and rolling back PHP and
MariaDB versions.

[0.2.1]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.2.1
[0.2.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.2.0
[0.1.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.1.0
