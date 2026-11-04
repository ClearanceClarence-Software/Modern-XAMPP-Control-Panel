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
- [ ] Logs window: follows `error.log` while Apache serves requests; the filter narrows lines; "Clear view" works.
- [ ] PHP window, Xdebug: Install downloads the build for the active PHP, `phpinfo()` shows Xdebug 3.x in debug
      mode; Turn off removes it from `phpinfo()`; a stale `php_xdebug.dll` shows the Update button. On a PHP
      branch PECL hasn't built for yet, the status names the branch instead of failing on the download.
- [ ] Databases: with MySQL running, the list matches phpMyAdmin; Export writes a `.sql` in `mysql_dumps`;
      Import into… a new name creates the database; Drop needs two clicks and the database is gone.
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
