# How to change the PHP version in XAMPP on Windows

XAMPP comes with one PHP version, and sooner or later a project wants another:

```
Your Composer dependencies require a PHP version ">= 8.4.0". You are running 8.2.12.
```

You do not have to reinstall XAMPP. PHP is a folder, and Apache loads it from there; replacing the folder
changes the version. This guide covers moving between PHP 8 versions (8.1, 8.2, 8.3, 8.4, …), which is the
common case and needs no change to Apache's configuration.

## What you need to know first

- **Thread Safe, x64.** XAMPP runs PHP inside Apache, and that only works with the **Thread Safe** build. The
  Non Thread Safe build will not load.
- **Your databases are not involved.** They live in `C:\xampp\mysql`, which this does not touch.
- **Keep the old folder.** It is your way back, and the source of your settings.

## Step 1: download the new PHP

Go to [windows.php.net/download](https://windows.php.net/download/) and download the **Zip** of the version you
want, from the block labeled **VS17 x64 Thread Safe** (or VS16 for PHP 8.0 to 8.3 builds that offer it).

## Step 2: swap the folder

1. **Stop Apache** in the XAMPP Control Panel.
2. Rename `C:\xampp\php` to `C:\xampp\php-old`.
3. Create a new, empty `C:\xampp\php` and unzip the download into it. `php.exe` must end up directly in
   `C:\xampp\php`, not in a subfolder.

## Step 3: bring your settings over

The new folder has no `php.ini` yet, and without one PHP runs with almost every extension off.

1. Copy `C:\xampp\php-old\php.ini` to `C:\xampp\php\php.ini`.
2. Open it and check that this line points at the new folder:

   ```
   extension_dir="C:\xampp\php\ext"
   ```

3. Extensions come and go between versions. If the old `php.ini` loads one that the new build does not have,
   PHP prints a warning on every request. Compare the `extension=` lines against the files in
   `C:\xampp\php\ext` and put a `;` in front of any that have no `php_<name>.dll` there.

XAMPP also keeps a few of its own things in the PHP folder. Copy these from `php-old` if you use them: the
`pear` folder, `browscap.ini` in `extras`, and any extension DLL you installed yourself (Xdebug, for one, but
see the note below).

> [!WARNING]
> Extension DLLs are built for one PHP version. An Xdebug or ImageMagick DLL from PHP 8.2 will not load in
> PHP 8.4; download the build for the new version instead of copying the old file.

## Step 4: fix the shared DLLs

This is the step that is easy to miss. Apache and PHP both use OpenSSL and a few other libraries, and Apache
loads its own copies first, from `C:\xampp\apache\bin`. When the new PHP was built against newer ones, `curl`
and `openssl` fail to load, with errors like:

```
PHP Warning:  PHP Startup: Unable to load dynamic library 'curl'
```

Copy these files from `C:\xampp\php` to `C:\xampp\apache\bin`, replacing the ones there **when the PHP copy is
newer** (compare the file versions under Properties › Details):

- `libcrypto-3-x64.dll` and `libssl-3-x64.dll`
- `libssh2.dll`
- `nghttp2.dll`

## Step 5: start and check

Start Apache. If it starts, create `C:\xampp\htdocs\info.php` with:

```php
<?php phpinfo();
```

Open `http://localhost/info.php`. The top line shows the new version, and "Loaded Configuration File" should
be `C:\xampp\php\php.ini`. Delete `info.php` afterward.

### If Apache does not start

- Open `C:\xampp\apache\logs\error.log` and read the last lines.
- **"Cannot load php8apache2_4.dll"** or **"The specified module could not be found"**: the zip was the Non
  Thread Safe build, or it was unzipped into a subfolder. Check that `C:\xampp\php\php8apache2_4.dll` exists.
- **Going back:** stop Apache, rename `php` to `php-new` and `php-old` back to `php`, and start Apache. You are
  where you began.

### Moving between PHP 7 and PHP 8

The file names changed with PHP 8, so Apache's configuration has to follow. In
`C:\xampp\apache\conf\extra\httpd-xampp.conf`, a PHP 7 setup has lines like:

```
LoadFile "C:/xampp/php/php7ts.dll"
LoadModule php7_module "C:/xampp/php/php7apache2_4.dll"
```

For PHP 8 they must read `php8ts.dll`, `php_module` and `php8apache2_4.dll`, and every `<IfModule php7_module>`
in that file becomes `<IfModule php_module>`.

## With Modern XAMPP Control Panel

I wrote [Modern XAMPP Control Panel](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel),
a free and open-source replacement for XAMPP's control panel, and this job is one of the reasons. Its
**Versions** window does the steps above:

- **Download and install.** Pick a PHP 8 branch; the Thread Safe x64 build is downloaded from windows.php.net
  and its checksum verified.
- **Settings carried over.** `php.ini`, PEAR and XAMPP's extras come along, extensions the new build lacks are
  commented out and listed, and the shared DLLs are synced with Apache.
- **Versions side by side.** The version you leave stays installed. Switching between installed versions takes
  seconds, and a Switch back button undoes the last change.
- **Updates announced.** When your PHP branch gets a new patch release, the panel says so and installs it with
  one button.

It handles PHP 8 only; for a move between PHP 7 and 8, the manual steps above are still the way.
