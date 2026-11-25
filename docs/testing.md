# Release test checklist

What to try by hand before publishing a release, on a real XAMPP install. The unit tests cover the parsers; this
covers what they can't: processes, services, Windows prompts and the UI. Fifteen minutes with a fresh `C:\xampp`.

## Panel

- [ ] Starts with no settings file; finds XAMPP; title shows the version and compile date.
- [ ] Start and Stop Apache and MySQL; PIDs and ports show; "Status change detected" lines appear.
- [ ] Stop MySQL logs a clean shutdown (not a kill). With a wrong root password, Force stop is offered.
- [ ] Start Apache with a broken `httpd.conf` line: the `httpd -t` error shows in the log and Apache isn't started.
- [ ] Occupy port 80 (IIS or `python -m http.server 80`): starting Apache names the program and PID.
- [ ] Quit with Apache running: the question appears; "Stop and quit" stops it; "Leave running" leaves it.
- [ ] Theme: Config › Theme and the sun/moon button switch at once; "Same as Windows" follows the Windows setting.
- [ ] Tray: minimizing hides to the tray (when on); the tray menu starts and stops modules; Show brings it back.

## Services and admin prompts

- [ ] Tick Apache's Service checkbox: one UAC prompt; the service installs; Start and Stop then drive the service.
- [ ] A second service action in the same session asks for no prompt (the helper is running).
- [ ] Decline the first prompt: the action is canceled with a log line, and the next action prompts again.
- [ ] Quit the panel: no `XamppControl.exe` is left in Task Manager (the elevated helper exits with it). Kill the
      panel from Task Manager: the helper exits within a second or two.
- [ ] Quit from the tray while minimized to the tray with Apache running: the window comes back with the question.

## Sites

- [ ] Add `myproject.test` with HTTPS, Apply: certificate issued, hosts file updated (one prompt), Apache restarts,
      `https://myproject.test` opens in Chrome or Edge with a padlock after "Trust".
- [ ] Apply again without changes: no admin prompt.
- [ ] Add a site whose folder has a typo in a config-breaking way (for example a folder with `"` in its path):
      Apache rejects it, the files are restored, and `httpd-vhosts.conf` is as before.
- [ ] Remove all sites and Apply: the block is gone from `httpd-vhosts.conf` and the hosts file.

## PHP and versions

- [ ] PHP window: change `memory_limit`, tick an extension, Save: `php.ini.bak` exists, Apache restarts,
      `phpinfo()` shows the new value.
- [ ] Versions: install a second PHP from the feed, switch between them, roll back.
- [ ] Versions: install a newer MariaDB with the dump on; databases are there afterwards; switch back shows the
      old data.
- [ ] Logs window: every log file is in the side list; choosing one shows its lines split into time, level
      and message (Apache error and access, PHP, MariaDB, Mailpit), and hovering a line shows it as written.
- [ ] Logs, live: with Follow on, new lines appear at the bottom while Apache serves requests; the filter and
      "Problems" narrow the lines and the Problems count follows the filter; "Clear view" works; Copy selected
      and Copy all shown give the raw lines.
- [ ] Logs, timeline: the bars cover the time the shown lines span, amber where the worst is a warning and
      red where there is an error; a click on a bar selects its first line and turns Follow off; clicking
      Follow goes back to the newest line.
- [ ] Logs, badges: a file with errors in the last 24 hours shows a red count, with only warnings an amber
      one; a file that isn't chosen updates its badge within about 15 seconds of a new error.
- [ ] PHP window, Xdebug: Install downloads the build for the active PHP, `phpinfo()` shows Xdebug 3.x in debug
      mode; Turn off removes it from `phpinfo()`; a stale `php_xdebug.dll` shows the Update button. On a PHP
      branch PECL hasn't built for yet, the status names the branch instead of failing on the download.
- [ ] Databases: with MySQL running, the list matches phpMyAdmin (names, table counts, collation); the size
      bars are relative to the largest database; `phpmyadmin` is labeled XAMPP and a database without tables
      "empty". The search narrows the list and keeps the chosen database chosen while it still matches.
- [ ] Databases, detail pane: Export writes a `.sql` in `mysql_dumps` and "Last export" shows it at once;
      Import .sql… with a new file name creates the database and selects it; Open in phpMyAdmin opens that
      database (with Apache stopped, a dialog offers to start Apache first, then opens it).
- [ ] Netstat: MariaDB's detail pane has "Open phpMyAdmin" (also on double-click); with Apache stopped a
      dialog offers to start Apache first.
- [ ] Databases, Drop: the first click shows the red note and the button reads "Yes, drop <name>"; choosing
      another database takes the question back; the second click drops it and the list refreshes.
- [ ] Databases with MySQL stopped, and with a wrong root password: the reason shows where the list would be.
- [ ] Databases with a root password set under Service and Port Settings: list and export still work.
- [ ] Versions, Mailpit: Install puts `mailpit.exe` in `xampp\mailpit` and `sendmail_path` in `php.ini`; the
      Mailpit row starts it; `php -r "mail('a@b.test','hi','body');"` shows up at `http://localhost:8025`.
- [ ] Sites: Editor opens the folder in the configured editor (Explorer when none is set); Terminal opens a prompt
      in the folder where `php -v` works.

## Port conflicts

- [ ] Start IIS (or `python -m http.server 80`) and click Start on Apache: the dialog names the holder; "End" /
      "Stop IIS" frees the port and Apache starts.
- [ ] Choose "Use ports 8080 and 4433" instead: `httpd.conf` and `httpd-ssl.conf` list the new ports, Config shows
      them, Apache starts, and `http://localhost:8080/` serves.
- [ ] Autostart with the panel minimized to the tray and a blocked port: no dialog, only the log lines.

## Updates

- [ ] An older build shows the "vX.Y.Z is available" link and the What's new window lists the newer releases.
- [ ] Install and restart: the download shows progress, the panel closes, the new version starts within a few
      seconds, and `XamppControl.exe` has the new version in its properties. Settings are intact.
- [ ] Install and restart with the exe in a folder that needs admin rights (Program Files): the UAC prompt appears
      and the swap still happens.
- [ ] Start the panel with no XAMPP (rename `C:\xampp` temporarily): the "XAMPP wasn't found" window lists the
      checked folders; Continue shows the banner over the module rows; Choose folder… works from both.

## Updates for PHP, MariaDB, Mailpit and Xdebug

- [ ] With an older patch of the active PHP branch installed (8.5.12 when 8.5.13 is out): after "Control Panel
      Ready" the log says "An update for PHP 8.5 is available", the Versions button shows a count, and the Apache
      row shows "↑ PHP 8.5.13". Clicking the row hint opens Versions.
- [ ] Versions: the active row has "Update to 8.5.13". One click installs it; the question "Remove PHP 8.5.12?"
      appears. Keep: the log says where 8.5.12 stays. Remove: the folder is gone and so is "Switch back".
- [ ] After the update the count and the row hint are gone without restarting the panel.
- [ ] Switch to another installed PHP branch and close Versions: no hint offers a version of the branch you left.
- [ ] A newer PHP branch than the active one: one plain log line ("PHP 8.6 is out"), once; not again on the next
      start. It never adds to the count.
- [ ] The same for MariaDB within its series; the remove question warns that the old build's copy of the
      databases goes with it.
- [ ] An older Xdebug than PECL's newest for this PHP: the PHP button shows a mark; installing it in the PHP
      window clears the mark.
- [ ] Offline at startup: no notices and no red lines. Untick "Check for new versions", Save: the notices clear.
- [ ] Parked builds in Versions show their size; one with a newer build of its branch says "replaced by …".

## Netstat

- [ ] Each program is listed once with its ports as labels; Apache shows 80 and 443 in the accent color.
- [ ] Selecting a program shows its path, ports, addresses and reach; the selection stays put across refreshes.
- [ ] "Open …" on Apache and on Mailpit opens the web page (Mailpit: port 8025, not 1025). Double-clicking the
      entry does the same.
- [ ] Search finds by port, name and PID. XAMPP and Blocking narrow the list; "Windows · n" adds Windows' own.
- [ ] With something on port 80 and Apache stopped: the program has the red "blocks Apache" label, shows even
      with Windows folded away, and its details explain it.
- [ ] End process on a program of your own (for example `python -m http.server 8000`) asks twice, ends it, and
      the entry shows dimmed as "closed" for one refresh. XAMPP's programs and Windows have no End button.

## Panel extras

- [ ] Start all starts the modules ticked as Autostart (Apache and MySQL when none is); Stop all stops what runs.
      Both are also in the tray menu.
- [ ] Config › Two-tone icons unticked, Save: every icon loses its orange wash at once; ticked brings it back.
- [ ] The taskbar, the title bar, the tray and Alt+Tab show the stack icon, sharp at each size.

## Look

- [ ] The main button of every dialog and pane (Export to .sql, Apply, Download and install, the confirm
      dialogs) is orange at rest, on hover and when pressed, in both themes; none shows Windows' blue.
- [ ] Netstat, Databases and Logs in the dark theme: the chosen entry, the labels and the level colors are
      readable.

## Pretend PC, screenshots and the website

- [ ] `XamppControl.exe --mock` opens on a pretend `C:\xampp`; starting, stopping and "installing" touch nothing
      real (check that your own Apache keeps running and no file under your `C:\xampp` changes).
- [ ] `.\tools\screenshots.ps1` writes twelve `*-light.png` into `docs\assets\screenshots` and no
      `.error.txt` files; `.\tools\brand.ps1` then writes `header.png` and `social-preview.png` at full size.
- [ ] `.\tools\site.ps1` builds the website and serves it locally; every page opens and the tour shows the
      screenshots.

## Languages

- [ ] Config › Language › Norsk bokmål, Save: the main window, side buttons, module rows and tray menu switch at
      once; windows opened afterwards (Sites, PHP, Versions, Netstat, Databases) are in Norwegian.
- [ ] Back to "Same as Windows": the panel follows the Windows display language (English on an English Windows).
- [ ] A copy of `en.json` saved as `lang\xx.json` next to the exe with `"$name": "Test"` shows up in the Language
      box; a broken JSON file there is ignored and the panel still starts.
- [ ] The log lines stay English in every language.

## Start menu

- [ ] Config › Show in the Start menu, Save: typing "XAMPP" in Windows search lists Modern XAMPP Control Panel
      with the stack icon, and it starts the panel.
- [ ] Untick and Save: the shortcut is gone. Delete the shortcut by hand instead: the box shows unticked and the
      panel doesn't recreate it on the next start.
- [ ] Move the exe to another folder with the box ticked and start it: the shortcut now starts the moved exe.
