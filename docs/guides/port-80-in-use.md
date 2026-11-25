# XAMPP: "Port 80 in use by 'Unable to open process' with PID 4"

Apache refuses to start, and the XAMPP log says something like:

```
Problem detected!
Port 80 in use by "Unable to open process" with PID 4!
Apache WILL NOT start without the configured ports free!
You need to uninstall/disable/reconfigure the blocking application
or reconfigure Apache and the Control Panel to listen on a different port
```

Apache needs port 80 (and 443 for HTTPS), and another program got there first. There are two ways out: free the
port, or move Apache to another one. Both are below.

## Step 1: find out who has the port

Open a Command Prompt and run:

```
netstat -ano | findstr ":80 "
```

The last column of the `LISTENING` line is the process ID (PID). Then look the PID up:

```
tasklist /fi "pid eq 1234"
```

| What you find | It is | Go to |
|---|---|---|
| PID `4`, named `System` | Windows itself, serving the port for IIS or another Windows feature | [PID 4: Windows holds the port](#pid-4-windows-holds-the-port) |
| `httpd.exe` | An Apache that is already running, often from an earlier session | End it in Task Manager, then start Apache again |
| `Skype.exe`, `nginx.exe`, `vmware-hostd.exe`, a game launcher, … | That program | Close it or change its port in its own settings, or [move Apache](#move-apache-to-another-port) |

Run the same two commands with `":443 "` if the message names port 443.

## PID 4: Windows holds the port

PID 4 is the Windows kernel's HTTP service. It listens on port 80 on behalf of something else, which is why
XAMPP cannot name the program. Almost always that something is **IIS**, Windows' own web server.

**Stop IIS for now.** In a Command Prompt opened with *Run as administrator*:

```
iisreset /stop
```

Start Apache again. If it starts, IIS was the one.

**Keep IIS from coming back.** IIS starts with Windows, so the error returns after a restart unless you do one
of these:

1. Press <kbd>Win</kbd>+<kbd>R</kbd>, run `services.msc`, find **World Wide Web Publishing Service**, stop it,
   and set its startup type to **Manual** (or **Disabled**).
2. Or remove IIS completely: **Control Panel › Programs › Turn Windows features on or off**, and untick
   **Internet Information Services**.

**If it is not IIS.** Other Windows components use the same HTTP service: SQL Server Reporting Services, Web
Deployment Agent Service, and BranchCache are the usual ones. This command lists what is registered on port 80:

```
netsh http show servicestate
```

Look for `:80/` in the output; the URL group names the component. Stop that service in `services.msc`.

## Move Apache to another port

If the other program has to keep port 80, give Apache a different one. Port 8080 is the usual choice.

1. Open `C:\xampp\apache\conf\httpd.conf` (in the XAMPP Control Panel: **Config** on the Apache row).
2. Change `Listen 80` to `Listen 8080`.
3. Change `ServerName localhost:80` to `ServerName localhost:8080`.
4. Save, and start Apache.

Your sites are now at `http://localhost:8080/`. The port is part of the address from now on, including for
phpMyAdmin: `http://localhost:8080/phpmyadmin/`.

For port 443, the file is `C:\xampp\apache\conf\extra\httpd-ssl.conf`: change `Listen 443` to `Listen 4433`,
`<VirtualHost _default_:443>` to `<VirtualHost _default_:4433>`, and `ServerName www.example.com:443` to use
`:4433` as well.

> [!NOTE]
> The stock XAMPP Control Panel keeps checking ports 80 and 443 and keeps warning about them after you move
> Apache. Tell it the new ports under **Config › Service and Port Settings**.

## With Modern XAMPP Control Panel

I wrote [Modern XAMPP Control Panel](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel),
a free and open-source replacement for XAMPP's control panel. This is what it does about this error:

- **It names the program.** Before Apache starts, the panel finds what holds each port, PID 4 included, and
  says whether Windows is holding it for IIS.
- **It offers the fix.** A dialog offers to end the program, to stop IIS, or to move Apache to 8080 and 4433.
  If you choose to move, the two config files and the panel's own port settings are changed together.
- **Netstat shows the rest.** Every listening port with the program behind it, and a mark on anything that
  blocks a stopped module.
