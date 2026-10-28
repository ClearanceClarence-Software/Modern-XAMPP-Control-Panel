# Security policy

## Supported versions

Only the [latest release](https://github.com/ClearanceClarence-Software/Modern-XAMPP-Control-Panel/releases/latest)
gets security fixes. If you run an older version, update first and check whether the problem is still there.

## Reporting a vulnerability

Please report security problems privately, not in a public issue:

1. Open the repository's **Security** tab.
2. Click **Report a vulnerability** and describe the problem.

Include the panel version (from the title bar), what an attacker could do, and the steps to reproduce it. A
log with "Show debug information" turned on helps; remove passwords and other private details first.

I aim to answer within a week. Once a fix is released, the advisory is published with credit to you, unless
you'd rather stay anonymous.

## What counts

The panel runs commands with admin rights and changes system files, so these are in scope:

- Anything that lets another program or user run code as administrator through the panel.
- The hosts file update, the virtual host and `php.ini` edits, and the version switching, if they can be made
  to write somewhere they shouldn't.
- The local certificate authority: the panel creating, storing or trusting certificates in an unsafe way.
- PHP or MariaDB downloads being installed without passing their SHA-256 check.
- Secrets leaking, such as the MySQL root password appearing in logs or command lines.

Out of scope:

- Vulnerabilities in XAMPP, Apache, PHP, MariaDB, phpMyAdmin or FileZilla themselves. Report those to their
  projects.
- A development setup reachable from the network. XAMPP is meant for local development; don't expose it to
  the internet.
- Someone who already has admin rights on the computer. They can do anything the panel can.

## By design

Some behavior is intentional and documented in the [README](README.md):

- The MySQL root password is stored in plain text in `%AppData%\XamppControl\settings.json`, for local
  development only.
- The local certificate authority's private key is stored in `%AppData%\XamppControl\local-ca.pfx` without a
  password, so the panel can issue certificates for new sites, the same way mkcert works. Anything running as
  your user can read it.
