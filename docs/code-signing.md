# Releasing and code signing

Releases are built, signed and uploaded on the maintainer's PC, not by GitHub Actions. GitHub Actions only checks
that every push builds (`.github/workflows/ci.yml`). This keeps the signing key off GitHub: the certificate is a
Certum Open Source Code Signing certificate whose key lives in Certum's cloud, reached through SimplySign Desktop.

## Making a release

1. Set `<Version>` in `XamppControl.csproj` and add the version's section to `CHANGELOG.md`:

   ```markdown
   ## [0.2.2] - 2026-11-02
   ```

2. Commit and push.
3. Start **SimplySign Desktop** and log in, so the certificate shows up in your certificate store.
4. From the repository folder, in PowerShell 7:

   ```powershell
   $env:GITHUB_TOKEN = gh auth token
   .\tools\release.ps1 -Sign -Upload
   ```

   The script checks formatting, publishes the single-file exe, signs it with SHA-256 and a Certum timestamp,
   verifies the signature, zips it into `artifacts\` with a `.sha256` file, and creates a **draft** release for the
   tag with the changelog section as its notes. Running it again replaces the files in the same draft.
5. Open the draft on GitHub, check it, and click **Publish release**. Publishing creates the tag.

Without `-Sign` the build is unsigned and its notes say so; without `-Upload` the zip stays in `artifacts\` for a
manual upload. `-CertificateThumbprint` picks a certificate when the store has more than one.

## Checking a signature

```powershell
Get-AuthenticodeSignature .\XamppControl.exe | Format-List Status, SignerCertificate
```

The publisher shows as "Open Source Developer" followed by the maintainer's name: Certum's fixed naming for its
open source certificate. Windows SmartScreen still builds reputation for a new signature as people download and
run it, so the first releases may show a warning that names the publisher instead of "Unknown publisher".

## Requirements on the release PC

- PowerShell 7, the .NET 10 SDK and the GitHub CLI (`winget install GitHub.cli`)
- `signtool.exe` from the Windows SDK (the "Signing Tools for Desktop Apps" feature is enough)
- SimplySign Desktop with the Certum certificate
