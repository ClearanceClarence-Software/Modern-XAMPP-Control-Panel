# Screenshots and the pretend PC

The pictures are the app itself, posed on a pretend PC, the same way SpaceSharp does it. Taking them changes
nothing on your computer.

## Take the screenshots

```powershell
.\tools\screenshots.ps1
```

It builds Release, then starts `XamppControl.exe --scene <name> --shot <file.png> --theme <light|dark>` once per
scene and theme. Each run poses the window, captures it off the screen with its title bar, and exits.

Without arguments it takes the **showcase set**: the twelve scenes the README's tour is built from, one per
feature worth showing (`main`, `new-versions`, `versions`, `versions-mariadb`, `port-conflict`, `netstat`,
`config-error`, `sites`, `php`, `databases`, `logs`, `config`), in the light theme. They go to
`docs\assets\screenshots` as `<scene>-light.png` and are committed, so run it again after a change that shows
in them.

```powershell
.\tools\screenshots.ps1 -Set all          # all 42 scenes, into artifacts\screenshots (not committed)
.\tools\screenshots.ps1 main,netstat      # just those scenes
.\tools\screenshots.ps1 -Themes dark      # the dark theme (light,dark for both)
.\tools\screenshots.ps1 -Lang nb          # another language from Lang\
.\tools\screenshots.ps1 -Size 1200x860    # the main window's client size
```

Windows only (the capture is GDI). Leave the middle of the screen free while it runs, since whatever is on top
of the window ends up in the picture. The monitor's scaling sets the pixel size; 125% or 150% looks right on
GitHub. If a scene fails, the reason is written to `<file>.png.error.txt` and the run goes on.

## The header and the social preview

```powershell
.\tools\brand.ps1
```

It renders `docs\brand\header.html` and `social.html` with Microsoft Edge into `docs\assets\header.png` and
`social-preview.png`. The product picture in them is `main-light.png` from the step above, so the brand images
always show the app as it is. The pages are plain HTML and CSS; open one in a browser to change the design.
GitHub's social preview is uploaded by hand under the repository's Settings.

## The scenes

**Main window**

| Scene | Shows |
|---|---|
| `main` | Apache, MySQL and Mailpit running. |
| `stopped` | Everything stopped. |
| `starting` | Apache in the middle of starting. |
| `services` | Apache and MySQL installed as Windows services. |
| `new-versions` | Updates found at startup: the count on Versions, the hints on the rows, the Xdebug mark on PHP. |
| `update` | The "new version" link in the header. |
| `config-error` | A syntax error in Apache's config, named in the log instead of a failed start. |
| `menu` | The Config menu of Apache's row, open. |
| `quit` | The "still running" question when closing, on the main window. |
| `no-xampp` | The "No XAMPP installation found" banner. |
| `xampp-missing` | The "XAMPP wasn't found" window on the main window. |
| `port-conflict` | The dialog when Windows (IIS) holds ports 80 and 443, on the main window. |

**Netstat**

| Scene | Shows |
|---|---|
| `netstat` | The programs that listen, XAMPP's first, with the selected one's details. |
| `netstat-xampp` | Only XAMPP's programs. |
| `netstat-filter` | Filtered on "apache". |
| `netstat-conflict` | The program that blocks Apache, selected, with the explanation. |

**Versions**

| Scene | Shows |
|---|---|
| `versions` | PHP: the active version and four more installed side by side. |
| `versions-mariadb` | MariaDB: the active version and three more installed. |
| `versions-tools` | FileZilla Server and Mailpit. |
| `versions-installing` | A PHP download in progress. |
| `versions-switched` | Right after switching PHP, with the button that switches back. |

**Sites, PHP, databases and logs**

| Scene | Shows |
|---|---|
| `sites` | Four sites, the local certificate authority trusted. |
| `sites-untrusted` | The same with the Trust button, before Windows trusts the authority. |
| `sites-empty` | No sites yet. |
| `php` | Settings, extensions and Xdebug on. |
| `php-no-xdebug` | Xdebug not installed, with the Install button. |
| `php-xdebug-update` | An older Xdebug with the update offered. |
| `databases` | Five databases, with "shop" chosen and its details beside the list. |
| `databases-drop` | The note that shows after the first click on Drop. |
| `databases-stopped` | MySQL not running. |
| `logs` | Apache's error log. |
| `logs-filter` | The same filtered on "error"; the timeline shows only those lines. |
| `logs-php` | PHP's error log. |

**Options**

| Scene | Shows |
|---|---|
| `config` | Config window. |
| `config-language` | Config window with the Language list open. |
| `ports` | Service and Port Settings. |
| `user-files` | User-defined files for the Config menus. |
| `log-options` | Log options. |

**The panel itself**

| Scene | Shows |
|---|---|
| `about` | About window. |
| `whats-new` | What's new with an update to install. |
| `whats-new-installing` | The update downloading. |
| `crash` | The crash report window with a sample report. |

Scene names take wildcards: `.\tools\screenshots.ps1 versions*` or `*-conflict`.

The tool windows are captured alone; the small dialogs are captured on top of the main window. The list
lives in `Views/MainWindow.Showcase.cs` (`Scenes` and `PoseSceneAsync`); add a scene there and to the script.

## The pretend PC

```powershell
XamppControl.exe --mock
dotnet run --project XamppControl.csproj -- --mock
```

starts the panel on the pretend PC so you can click around; it looks like a normal install at `C:\xampp`. Downloads are only
played: the bar runs, then the status says nothing was installed. Start and
Stop flip a module's state and write the usual log lines. `--scene <name>` without `--shot` poses a scene and
leaves it open. `--theme dark` and `--lang nb` work with both.

What it is (`Core/MockMachine.cs`):

- A folder in `%TEMP%\XamppControl\mock\xampp`, rebuilt on every start: empty executables, real `php.ini`,
  `httpd.conf`, `my.ini` and log files, which the PHP, Sites, Logs and Config windows read as usual. Paths are
  shown as `C:\xampp`.
- Made-up processes, ports, databases, download feeds and versions.
- Nothing real is touched: no program is started or ended, nothing runs as admin, nothing is downloaded, and the
  certificate store, the hosts file, the Start menu and your settings file are left alone. Settings live in
  memory and are never saved.
- Buttons that would need the real thing (installing a version, exporting a database, applying sites) report a
  failure instead of doing anything.

To change what the pictures show (other sites, databases, versions), edit `Core/MockMachine.cs`. These switches
are for developers and are not in any help text.
