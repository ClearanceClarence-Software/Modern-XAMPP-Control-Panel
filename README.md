<p align="center">
  <img src="docs/assets/header.png" alt="Modern XAMPP Control Panel: a faster, safer control panel for XAMPP on Windows" width="100%">
</p>

<p align="center">
  <a href="https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/ClearanceClarence-Software/Modern-XAMPP-Control-Panel?color=F47A26"></a>
  <a href="https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/total?color=F47A26"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/github/license/ClearanceClarence-Software/Modern-XAMPP-Control-Panel"></a>
</p>

A drop-in replacement for the XAMPP Control Panel on Windows. It keeps the layout you already know
(module rows, Start/Stop, Admin, Config, Logs) and fixes the things that hurt: databases corrupted
by hard kills, cryptic "shutdown unexpectedly" errors, and no way to change PHP or MariaDB versions.

> [!NOTE]
> This is an unofficial, community project. It is not affiliated with or endorsed by Apache Friends.
> XAMPP and the XAMPP logo are trademarks of Apache Friends.

<!--
Screenshots: save these in docs/assets/ (about 1100 px wide, PNG), then remove this comment.
<p align="center">
  <img src="docs/assets/screenshot-light.png" alt="Main window, light theme" width="49%">
  <img src="docs/assets/screenshot-dark.png" alt="Main window, dark theme" width="49%">
</p>
<p align="center">
  <img src="docs/assets/screenshot-sites.png" alt="Sites window" width="49%">
  <img src="docs/assets/screenshot-versions.png" alt="Versions window" width="49%">
</p>
-->

## Why use it

| | XAMPP Control Panel | Modern XAMPP Control Panel |
|---|---|---|
| Stopping MySQL/MariaDB | Ends the process | Clean shutdown with `mariadb-admin`, so InnoDB flushes its logs |
| When a server won't start | "Shutdown unexpectedly" | Shows the last lines of the error log in the panel |
| Ports in use | Generic warning | Names the program and PID holding the port, before starting |
| PHP / MariaDB versions | Whatever came with XAMPP | Install, update and switch between several versions |
| Versions at a glance | No | Apache, PHP and MariaDB versions read from the binaries |
| Local sites | Edit `httpd-vhosts.conf` and the hosts file by hand | `https://myproject.test` in one click, with a trusted certificate |
| php.ini | Open it in Notepad | Common settings and every extension on one page |
| Broken Apache config | "Shutdown unexpectedly" | `httpd -t` runs first and names the line |
| Look and feel | 2010s VCL | Modern UI with light and dark themes, same familiar layout |

## Features

**Familiar control panel.** Module rows for Apache, MySQL, FileZilla, Mercury and Tomcat with
Service, PID(s), Port(s), Start/Stop, Admin, Config and Logs, plus Config, Netstat, Shell, Explorer,
Services and Help buttons, just like the original. A tray icon shows what runs, starts and stops
modules from its menu, and keeps the panel out of the taskbar when minimized.

**Safe MySQL/MariaDB handling.** Stop always tries a clean shutdown first. A hard kill is only
offered as **Force stop** when the clean shutdown fails, and it tells you that InnoDB will run crash
recovery on the next start.

**Startup failures explained.** Before Apache starts, the panel checks its configuration with
`httpd -t` and shows the exact error instead of starting a server that dies. If Apache or MySQL
exits right after starting anyway, the panel shows the last lines of `error.log` or
`mysql_error.log`, so you see *why* without opening files.

**Port conflict finder.** Before a module starts (and optionally when the panel opens), the panel
checks its ports and reports exactly which program holds them:
`Port 80 in use by "C:\Program Files\Skype\Skype.exe" with PID 4120!`

**PHP and MariaDB versions.** The **Versions** window lists every installed build and lets you:

- **Install** a new version straight from windows.php.net or downloads.mariadb.org (SHA-256
  verified), or from a zip you downloaded yourself.
- **Switch** between installed versions in seconds; the server is stopped and restarted for you.
- **Switch back** after any install or switch, and **remove** versions you no longer need.

**FileZilla Server upgrade.** XAMPP still ships FileZilla Server 0.9.41 from 2012. The Versions
window opens the official download page and runs the installer you downloaded: it stops the old
server, installs FileZilla Server 1.x as a Windows service, and the FileZilla row then starts, stops
and administers the new one. Later 1.x updates go the same way and keep your users. FileZilla's
download links expire, so the panel can't download it for you, and 1.x can't read 0.9's settings,
so FTP users are set up again in its Admin program.

**Sites with HTTPS.** The **Sites** window gives each project its own address, such as
`https://myproject.test`. Pick a name and a folder, and Apply:

- writes the virtual hosts to `httpd-vhosts.conf` (in a marked block; your own entries are left
  alone) and tests the config before keeping it,
- adds the names to the Windows hosts file (one admin prompt),
- issues a certificate for localhost and every site from a certificate authority that exists only
  on your computer, and asks Windows to trust it, so Chrome and Edge show the padlock,
- restarts Apache. `localhost` keeps serving `htdocs`.

**PHP settings.** The **PHP** window edits the `php.ini` values everyone changes (memory limit,
upload size, post size, execution time, input vars, display errors, time zone) and lists every
extension in `php\ext` with a check box. Saving keeps the previous file as `php.ini.bak` and
restarts Apache.

**Update notice.** The panel checks GitHub for newer releases when it starts. When there is one, a
link in the header opens the release notes of every version since yours, with a button to download
the newest. You can turn this off in Config.

**Netstat.** Listening TCP sockets with address, port, PID and process, refreshed every 3 seconds.
New sockets show in green, closed ones in dark red, and XAMPP's own processes are marked.

**XAMPP-style configuration.** Editor, browser, XAMPP folder, autostart per module, start
minimized, light or dark theme (or follow Windows; the sun/moon button in the header switches it), minimize to tray, update check, port check on startup, debug output, plus Service and Port Settings, User Defined Files
and Log Options.

## Download

1. Download `ModernXamppControlPanel-<version>-win-x64.zip` from the
   [latest release](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/latest).
2. Unzip `XamppControl.exe` anywhere, for example next to `xampp-control.exe` in `C:\xampp`.
3. Run it. It finds XAMPP in its own folder, the folder above it, or `C:\xampp`, and asks for the
   folder if none of those has an install.

It's a single self-contained exe: no installer and no .NET runtime to install. See the
[changelog](CHANGELOG.md) for what's new in each version.

> [!TIP]
> Windows SmartScreen may warn about an unrecognized app the first time. Choose
> **More info › Run anyway**. Releases are code-signed; each release's notes name the signer, and
> [docs/code-signing.md](docs/code-signing.md) shows how to check the signature.

### Requirements

- Windows 10 or 11, x64
- XAMPP for Windows (any recent version)

## How versions work

The active build always lives where XAMPP expects it (`C:\xampp\php` and `C:\xampp\mysql`), so
Apache's config and `my.ini` keep working. Other builds are kept in `C:\xampp\versions\php\<version>`
and `C:\xampp\versions\mariadb\<version>`, and switching swaps the folders.

**PHP.** Only PHP 8 Thread Safe x64 builds are supported, because XAMPP runs PHP as an Apache
module. A new version carries over your `php.ini`, PEAR and XAMPP's extras. Extensions the new build
doesn't include are commented out in `php.ini` and listed in the log. On every install or switch,
the DLLs PHP shares with Apache (OpenSSL, libssh2, nghttp2, …) are synced, using the newer copy of
each, so `php_curl` and `mod_ssl` both load.

**MariaDB.** Each version keeps **its own databases**. They can't share one data folder: a newer
server converts the data files so older servers can't open them. Installing a newer version:

1. optionally saves an SQL dump of all databases to `C:\xampp\mysql_dumps`,
2. does a full clean shutdown (`innodb_fast_shutdown=0`),
3. copies the data folder, `my.ini`, scripts and batch files into the new version,
4. starts it and runs `mariadb-upgrade`, repairing Aria system tables if needed.

The previous version stays installed with its own untouched copy, so switching back gives you the
exact state from before the update. Downgrades are refused.

## Settings and files

| What | Where |
|---|---|
| Settings | `%AppData%\XamppControl\settings.json` |
| Optional log file | `%AppData%\XamppControl\xampp-control.log` |
| Local certificate authority | `%AppData%\XamppControl\local-ca.pfx` (private) and `local-ca.crt` (public) |
| Site certificate | `C:\xampp\apache\conf\ssl.crt\modern-xampp-local.crt` and `ssl.key\modern-xampp-local.key` |
| Downloads | `%TEMP%\XamppControl` |
| Installed versions | `C:\xampp\versions\` |
| SQL dumps | `C:\xampp\mysql_dumps\` |

The local certificate authority's private key (`local-ca.pfx`) can sign certificates your browser
trusts, the same way mkcert works. It never leaves your computer, but keep it private, and remove
"Modern XAMPP Control Panel Local CA" from Windows' certificate manager (`certmgr.msc`, Trusted Root
Certification Authorities) if you stop using the panel.

The MySQL root password (Config › Service and Port Settings) is only used for clean shutdowns,
dumps and upgrades. It is stored in plain text in `settings.json`, which is fine for a local
development machine but worth knowing.

## Troubleshooting

**MySQL won't stop cleanly.** Set the root password under Config › Service and Port Settings. If
the server really is stuck, use **Force stop**.

**A module runs as a Windows service.** Updates and version switches need the module to run
normally. Untick its Service checkbox first (this asks for admin rights).

**A site shows "Not secure".** Click **Trust** in the Sites window and answer Yes to Windows'
warning. Firefox keeps its own certificate list: import `local-ca.crt` there, or set
`security.enterprise_roots.enabled` to `true` in `about:config`.

**A site name doesn't resolve.** The hosts file is only changed after the admin prompt. If you
canceled it, or an antivirus blocked the change, click **Apply** again.

**Apache won't start after a PHP change.** The panel shows the reason from `error.log`. Use
**Switch back** in the Versions window to return to the previous PHP.

## Building from source

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel.git
cd Modern-XAMPP-Control-Panel
dotnet run
```

Self-contained single-file build, as published in releases:

```powershell
dotnet publish -p:PublishProfile=win-x64
# bin\publish\win-x64\XamppControl.exe
```

Releases are built, signed with a Certum code-signing certificate and uploaded as a draft on the
maintainer's PC with `tools/release.ps1`; GitHub Actions only checks that each push builds. See
[docs/code-signing.md](docs/code-signing.md).

## Contributing

Issues and pull requests are welcome; the issue forms ask for the log, which usually shows the
cause. The code style is defined in `.editorconfig` (Allman braces, 120 columns, `_camelCase` private
fields, analyzers at `latest-recommended`) and explained, with examples, in
[CODING_STANDARDS.md](CODING_STANDARDS.md). Before opening a pull request, run:

```powershell
dotnet format --verify-no-changes
dotnet build
```

and make sure neither reports anything.

## Security

Please report security problems privately through the repository's **Security** tab, not in a public issue.
[SECURITY.md](SECURITY.md) says what counts and what to include.

## License

[MIT](LICENSE) © ClearanceClarence.

XAMPP and the XAMPP logo are trademarks of [Apache Friends](https://www.apachefriends.org/).
Apache, MariaDB, MySQL and PHP are trademarks of their respective owners.
