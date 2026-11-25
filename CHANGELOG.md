# Changelog

All notable changes to Modern XAMPP Control Panel. The release script copies the section for each version into
its GitHub release.

## [0.5.0] - 2026-11-25

### Added

- **Updates for the installed PHP, MariaDB, Mailpit and Xdebug are announced.** At startup, and once a day
  while the panel stays open, it asks the release feeds for newer builds of what is installed ("An update for
  PHP 8.5 is available: 8.5.13"). What it finds shows as a count on the Versions button, as a clickable
  "↑ 8.5.13" next to the version on the module's row, in the tray icon's tooltip and menu, and as one line in
  the log; installing it clears the notice. A newer Xdebug is marked on the PHP button, where it is installed.
  A whole new PHP branch (8.6, 9.0) is news, not an update: the log says it once per branch, after that it
  only stays in the Versions tooltip. All of it follows the existing "Check for new versions" setting and
  stays silent when offline.
- **"Update to 8.5.13" on the active row.** In Versions, the active PHP and MariaDB rows get an update button
  when their own branch or series has a newer build; one click downloads, verifies and installs it, and the
  build it replaces stays installed to switch back to.
- **"Remove the previous one?" after an update.** When an update stays within the branch (8.5.12 to 8.5.13),
  Versions asks whether to remove the build it replaced, since the new one likely fixes bugs or security
  issues in it. For MariaDB the question says that the old build's copy of the databases goes with it.
- **Old builds are easier to clean up.** In Versions, a parked build shows the disk space its folder takes,
  and one that has a newer build of its own branch installed is marked "replaced by 8.5.13".
- **Start all and Stop all**, in the header of the module list and in the tray menu. Start all starts the
  modules ticked as Autostart in Config, or Apache and MySQL when none is ticked.
- **Pretend PC and screenshot tool**, built like SpaceSharp's. `XamppControl.exe --mock` runs the panel against
  a made-up XAMPP in the temp folder; nothing on the PC is started, stopped, downloaded or saved.
  `--scene <name> --shot <file.png>` poses one of 42 scenes on it (the main window in every state, several
  PHP and MariaDB versions installed side by side, a download in progress, the options windows, the dialogs),
  captures the window off the screen with its title bar, and exits; `tools\screenshots.ps1` takes them, and
  accepts wildcards such as `versions*`. See `docs/screenshots.md`.

### Changed

- **Netstat, redesigned.** One entry per program instead of one row per socket, with its ports as labels and
  a detail pane beside the list: path, ports, addresses, who can reach it, and the actions (open in the
  browser, show in folder, copy, end process). XAMPP's programs come first, Windows' own stay folded away
  until asked for, and the list only redraws when something changed. MariaDB's entry has "Open phpMyAdmin".
- **Logs, redesigned.** All log files sit in a side list, each with a badge counting its errors (or warnings)
  from the last 24 hours, so you see which log to open. Lines are split into time, a colored level and the
  message instead of one raw string; errors are tinted, routine notices grayed. A "Problems" switch hides
  everything but warnings and errors. A timeline above the lines shows when they were written, red where
  there were errors, and a click on a bar jumps there. "Follow" keeps the newest line in view and can be
  switched off to read in peace.
- **Databases, redesigned.** The same shape as Netstat: a searchable list with a size bar per database, and
  the chosen one beside it with its table count, size, collation and last export. Export, import, "Open in
  phpMyAdmin" and Drop live in that pane instead of three buttons on every row. Drop still takes two clicks,
  and now says what it deletes right above the button. XAMPP's own `phpmyadmin` database and empty ones are
  labeled and grayed, so yours stand out.
- **phpMyAdmin with Apache stopped.** "Open in phpMyAdmin" in Databases and Netstat offers to start Apache
  first when it isn't running, then opens the page.
- **A new icon.** Three floating slabs with a green status light: the stack (Apache, MariaDB, PHP), running.
  It no longer looks like XAMPP's own orange square, so the two can sit side by side on the taskbar. Sizes of
  32 px and below use a simplified drawing.
- **A website.** `site/` holds a multi-page site for GitHub Pages: the tour, every feature, the download with
  the newest release, the changelog, the reference, and step-by-step guides for the XAMPP errors people search
  for (MySQL shutdown unexpectedly, port 80 in use, changing the PHP version, HTTPS on localhost). It is built
  from the README, `docs/guides` and this changelog by `site/build.py`, so nothing is written twice, and
  published from its own branch.
- **New README and brand images.** The README walks through the features with real screenshots;
  `tools\screenshots.ps1` takes that showcase set by default, in the light theme (`-Set all` for every
  scene). The header and the social preview are built around the main window's screenshot by
  `tools\brand.ps1`, from the pages in `docs\brand`.
- **New icons, drawn for the panel.** A two-tone set of its own: line work in the text color over a soft wash
  of the panel's orange, with its own pictures (a hex nut for Config, linked nodes for Netstat, a lifebuoy
  for Help, a power button for Quit). Config has a "Two-tone icons" box; unticked, they are plain line icons.

### Fixed

- **Main buttons are the panel's orange everywhere.** The main button of a dialog or pane could show in the
  Windows accent color (blue) until the mouse was over it.

## [0.4.0] - 2026-11-16

### Added

- **Start menu shortcut.** Config has a "Show in the Start menu" box. It adds a shortcut for the current user, so
  typing "XAMPP" in Windows search finds the panel, and removes it again when unticked. If the exe is moved, the
  shortcut is pointed at the new place the next time the panel starts.
- **Languages.** The panel can be translated. Every text lives in one JSON file per language in `Lang/`; English
  and Norwegian Bokmål ship with it, and Config has a Language box (it follows Windows by default and switches
  open windows at once). A translation can be tried by dropping a `<code>.json` into a `lang` folder next to the
  exe, without building. `docs/translating.md` explains how to add a language. The log stays in English in every
  language, so bug reports read the same.

### Changed

- Texts with counts ("1 table", "3 tables") and sentences with values are single translatable entries instead of
  glued fragments; tests fail when a text used in the app is missing from `Lang/en.json` or a translation's
  placeholders differ from English.

## [0.3.2] - 2026-11-04

### Fixed

- Xdebug install failed with "PECL lists no stable Xdebug release": the version list now comes from the
  downloads.php.net folder the zips live in, and the newest version that has a build for your PHP branch is
  picked (a brand-new PHP branch gets the previous Xdebug instead of a download error).

## [0.3.1] - 2026-11-03

### Added

- **Update from the panel.** The What's new window has an "Install and restart" button: it downloads the
  release's signed zip, checks its SHA-256 against the published checksum, swaps `XamppControl.exe` after the
  panel closes and starts the new version. Settings and XAMPP are untouched. Running modules keep running.
- **About window.** Version, build date, runtime, exe location, a "Check for updates" button, links to GitHub,
  releases, the issue form and the license, and "Copy build details" for bug reports. Opens from the ⓘ button in
  the header.
- **Clear message without XAMPP.** When no XAMPP is found, a window lists the folders that were checked and offers
  to download XAMPP, choose its folder, or continue; the panel also shows a banner with the same choices until a
  folder is set.

### Changed

- **New icon.** The panel has its own server-rack icon (exe, window, tray and header) instead of borrowing
  XAMPP's; `tools\Get-XamppIcon.ps1` is gone.
- Built on Avalonia 12 (was 11.3). No visible change; text boxes use the new `PlaceholderText` property and the
  clipboard calls the new extension methods.

## [0.3.0] - 2026-11-02

### Added

- **Logs window.** Follows Apache, PHP and MariaDB log files live, like `tail -f`, with a filter box and errors and
  warnings colored. Rotated or cleared files are picked up again.
- **One admin prompt per session.** The first action that needs admin rights (starting or stopping a Windows
  service, the hosts file) starts a small elevated helper that serves the rest of the session, so you answer the
  UAC prompt once instead of every time. If you decline, that action is canceled and later ones ask one by one,
  as before.
- **Crash reports.** An error the panel can't recover from now shows a window with the details, buttons to copy
  them and open an issue, and the choice to keep running or quit. The report is also saved to `crash.log` in the
  settings folder.
- **Quit asks about running modules.** Closing the panel with Apache or MySQL running offers to stop them cleanly
  first or leave them running.
- **Databases window.** Lists the databases on the running server with table counts and sizes, exports any of
  them to a timestamped `.sql` dump in `xampp\mysql_dumps` (routines, events and triggers included), imports a
  `.sql` file into a new or existing database, and drops databases after a second click.
- **Xdebug button.** The PHP window installs the Xdebug build that matches the active PHP from PECL, turns it on
  (debug mode, start on request, port 9003) or off, and offers newer builds when PECL has one.
- **Mailpit mail catcher.** The Versions window installs Mailpit into `xampp\mailpit` and sets `sendmail_path`
  in `php.ini`, so `mail()` delivers to it. A Mailpit module row starts and stops it; Admin opens the inbox at
  `http://localhost:8025`. Its ports are in Service and Port Settings.
- **Port conflict fixes.** When a module can't start because another program holds its port, a dialog offers to
  end that program, stop IIS when Windows holds the port, or move Apache to 8080/4433 (or MySQL to 3307) with the
  config files and the panel's settings updated.
- **Editor and Terminal per site.** The Sites window opens a site's folder in the configured editor or in a
  command prompt with `php` and `mysql` on PATH.
- **Netstat redesigned.** A filter box (port, address, PID, program or service), "XAMPP only" and IPv6 toggles,
  Pause, sortable columns, and tiles for the socket count, XAMPP's sockets and blocked XAMPP ports. Each row names
  what the port is for (Apache, MySQL, HTTP, Remote Desktop...), who can reach it, and warns when a foreign program
  blocks a stopped module; Open, Folder, Copy and End buttons act on the row.
- **Tests.** An xUnit project (`tests/XamppControl.Tests`) covers the release-feed parsing, `php.ini` editing,
  the virtual host and hosts-file blocks, port rewriting and the version parsing; CI and the release script run it.

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

[0.5.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.5.0
[0.4.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.4.0
[0.3.2]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.3.2
[0.3.1]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.3.1
[0.3.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.3.0
[0.2.1]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.2.1
[0.2.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.2.0
[0.1.0]: https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/tag/v0.1.0
