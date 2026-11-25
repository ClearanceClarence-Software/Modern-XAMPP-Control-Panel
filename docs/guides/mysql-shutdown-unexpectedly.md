# XAMPP: "Error: MySQL shutdown unexpectedly"

You click Start next to MySQL, and a few seconds later the XAMPP log turns red:

```
Error: MySQL shutdown unexpectedly.
This may be due to a blocked port, missing dependencies,
improper privileges, a crash, or a shutdown by another method.
```

That message lists every possibility and tells you nothing. In practice it is almost always one of two things,
and you can tell which in a minute.

## Step 1: read the real error

The actual reason is in MariaDB's own log, not in the control panel:

```
C:\xampp\mysql\data\mysql_error.log
```

Open it and scroll to the end. (In the XAMPP Control Panel: the **Logs** button on the MySQL row.) What you find
there decides what to do next.

| The last lines mention | It means | Go to |
|---|---|---|
| `Can't start server: Bind on TCP/IP port` or `Do you already have another mysqld server running on port: 3306` | Something else is using port 3306 | [Port 3306 is taken](#port-3306-is-taken) |
| `InnoDB: ... corrupt`, `Plugin 'InnoDB' init function returned error`, `Aria recovery failed`, `Unknown/unsupported storage engine: InnoDB` | The data files were damaged, usually by a hard stop | [The data files are damaged](#the-data-files-are-damaged) |

## Port 3306 is taken

Another MySQL or MariaDB is already running: often a second XAMPP, a MySQL installed as a Windows service, or a
`mysqld.exe` left over from a previous session.

1. Open Task Manager, go to **Details**, and look for `mysqld.exe` or `mariadbd.exe`. If one is running and it is
   not the one you just tried to start, end it and start MySQL again.
2. If it comes back by itself, it is a Windows service. Press <kbd>Win</kbd>+<kbd>R</kbd>, run `services.msc`,
   find **MySQL** (or **MySQL80**, **MariaDB**), stop it, and set its startup type to **Manual**.
3. If you need both servers, give XAMPP's another port instead: in `C:\xampp\mysql\bin\my.ini`, change both
   `port=3306` lines (under `[client]` and `[mysqld]`) to `3307`, and use that port in your application's
   database settings.

## The data files are damaged

This is the common one. It happens when MySQL is ended instead of shut down: Windows restarted for updates, the
PC lost power, or the process was killed (from Task Manager, or by a stop script that kills it) while it was
still writing.

The fix below restores MariaDB's own system tables from the copy XAMPP ships with and keeps your databases.

> [!WARNING]
> Do step 2 before anything else. With the copy of the `data` folder, every other step can be undone.

1. **Stop MySQL** and close the XAMPP Control Panel.
2. **Copy** `C:\xampp\mysql\data` to somewhere safe, for example `C:\xampp\mysql\data-broken`. This is your
   backup of everything, damaged or not.
3. **Rename** `C:\xampp\mysql\data` to `data-old`.
4. **Copy** `C:\xampp\mysql\backup` and name the copy `data`. This is a clean, empty set of data files.
5. From `data-old` into the new `data`, **copy your own database folders**: every folder that is one of your
   databases. Do **not** copy `mysql`, `performance_schema`, `phpmyadmin` or `test`; those are the system
   folders that step 4 just replaced.
6. From `data-old` into the new `data`, **copy the file `ibdata1`**, replacing the one that is there. Your
   InnoDB tables do not work without it. Leave the `ib_logfile` and `aria_log` files alone.
7. Start XAMPP and start MySQL.

If it starts, check your databases in phpMyAdmin, then export each one (**Export** tab) so you have a real
backup. Two things are different afterward:

- **Database users are reset.** The users and passwords lived in the `mysql` folder you replaced. `root` has
  no password again, as on a fresh XAMPP; recreate any other users your projects log in with.
- **`data-old` and `data-broken` are still there.** Delete them once you are sure everything is back.

### If it still does not start

Read the end of `mysql_error.log` again; it will name what it could not read.

- **It names one table or database.** Move that database's folder out of `data` and start again. If the server
  starts, the damage is in that one database, and the rest are safe to export.
- **InnoDB itself will not start.** Add `innodb_force_recovery = 1` under `[mysqld]` in
  `C:\xampp\mysql\bin\my.ini` and start again. If it starts, export every database right away, then remove the
  line; a server in recovery mode is for getting data out, not for working in. Raise the number one step at a
  time (up to 6) only if the lower one fails, and expect more to be missing each step.

## How to stop it happening again

- **Stop MySQL before you shut down Windows**, and wait for it to finish.
- **Do not use Task Manager** to end `mysqld.exe` unless it is truly stuck.
- **Keep SQL exports** of anything you care about. The `data` folder is not a backup of itself.

## With Modern XAMPP Control Panel

I wrote [Modern XAMPP Control Panel](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel)
because of this error, so this is where it differs. It is free and open source, and it is a replacement for the
control panel only; your XAMPP stays as it is.

- **Stop is a real shutdown.** The Stop button asks MariaDB to shut down (`mariadb-admin shutdown`) and waits
  for it. A hard kill is only offered if that fails, and it says what a hard kill risks.
- **The reason is in front of you.** When MySQL does die, the last lines of `mysql_error.log` are shown in the
  panel, so step 1 above is already done.
- **A taken port is named.** If something holds 3306, the panel says which program it is and offers to stop it
  or move MySQL to a free port.

It does not repair damaged data files for you; the steps above are still the fix for a database that is already
broken.
