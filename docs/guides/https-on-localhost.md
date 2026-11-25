# HTTPS for local sites in XAMPP

You open your local project over `https://` and the browser stops you:

```
Your connection is not private
NET::ERR_CERT_AUTHORITY_INVALID
```

XAMPP's Apache does speak HTTPS, but with a certificate that nobody trusts and that is issued for `localhost`
only. For a padlock on a name like `https://myproject.test`, three things have to exist: a certificate
authority your computer trusts, a certificate from it for your site's name, and an Apache virtual host that
uses that certificate. This guide sets up all three.

## Pick a good name

Use a name ending in **`.test`**, such as `myproject.test`. That ending is reserved for testing and will never
be a real website.

- Avoid `.dev` and `.app`: browsers force HTTPS on them and they are real domains.
- Avoid `.local`: it is used by network discovery (Bonjour) and resolves slowly or wrongly on some networks.

## Step 1: make a certificate authority your computer trusts

The simplest tool for this is [mkcert](https://github.com/FiloSottile/mkcert). Download `mkcert-v*-windows-amd64.exe`
from its releases page, rename it to `mkcert.exe`, and in a Command Prompt in that folder run:

```
mkcert -install
```

Windows asks whether to trust a new certificate authority; answer **Yes**. From now on, certificates that mkcert
makes on this computer are trusted by Chrome and Edge. Firefox keeps its own list: either install `certutil` as
mkcert's page describes, or set `security.enterprise_roots.enabled` to `true` in `about:config`.

> [!WARNING]
> The authority's private key can sign a certificate for any website, and your browser will trust it. Never
> share the files in the folder that `mkcert -CAROOT` prints.

## Step 2: make a certificate for your site

```
mkcert myproject.test
```

This writes `myproject.test.pem` (the certificate) and `myproject.test-key.pem` (its key). Move both to a
folder Apache can read, for example `C:\xampp\apache\conf\ssl.crt\`.

## Step 3: point the name at your computer

Open Notepad with *Run as administrator*, open `C:\Windows\System32\drivers\etc\hosts` (choose "All files" in
the dialog), and add:

```
127.0.0.1    myproject.test
```

## Step 4: add the virtual host

Open `C:\xampp\apache\conf\extra\httpd-vhosts.conf` and add at the end:

```apache
<VirtualHost *:80>
    ServerName myproject.test
    DocumentRoot "C:/xampp/htdocs/myproject"
</VirtualHost>

<VirtualHost *:443>
    ServerName myproject.test
    DocumentRoot "C:/xampp/htdocs/myproject"
    SSLEngine on
    SSLCertificateFile "conf/ssl.crt/myproject.test.pem"
    SSLCertificateKeyFile "conf/ssl.crt/myproject.test-key.pem"
    <Directory "C:/xampp/htdocs/myproject">
        AllowOverride All
        Require all granted
    </Directory>
</VirtualHost>
```

Use forward slashes in the paths. If the project is outside `htdocs`, the `<Directory>` block is what allows
Apache to serve it.

One more thing, or `http://localhost` stops showing your `htdocs` folder: the first virtual host in the file
becomes the default for every name. Put this one above the others:

```apache
<VirtualHost *:80>
    ServerName localhost
    DocumentRoot "C:/xampp/htdocs"
</VirtualHost>
```

## Step 5: test and restart

Before restarting, let Apache check the file. In the XAMPP Control Panel click **Shell**, then run:

```
httpd -t
```

`Syntax OK` means it is safe to restart Apache. Anything else names the file and the line to fix. Then open
`https://myproject.test`.

### If it does not work

- **Still "not private".** The browser was open while you ran `mkcert -install`; close every window of it and
  try again. In Firefox, see step 1.
- **"This site can't be reached".** The hosts line is missing or was not saved (Notepad must run as
  administrator), or Apache is not listening on 443. Check that `Include conf/extra/httpd-ssl.conf` is not
  commented out in `httpd.conf`.
- **The wrong site shows.** The `ServerName` does not match the address, so Apache fell back to the first
  virtual host.

## With Modern XAMPP Control Panel

I wrote [Modern XAMPP Control Panel](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel),
a free and open-source replacement for XAMPP's control panel. Its **Sites** window does all five steps:

- **A name and a folder.** Type `myproject.test`, pick the folder, tick HTTPS, press Apply.
- **Everything written for you.** The virtual hosts go into a marked block in `httpd-vhosts.conf`, the name
  into the hosts file, and a certificate comes from a local authority that the panel creates and Windows
  trusts, the same way mkcert works.
- **Tested before the restart.** `httpd -t` runs first; a mistake is shown instead of a server that will not
  start. `localhost` keeps serving `htdocs`.
