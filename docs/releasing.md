# Releasing

Eight packages are published from one version:

`LifeOS.Pdf`, `LifeOS.Pdf.Signing`, `LifeOS.Pdf.Rendering`,
`LifeOS.Pdf.Barcode.Scanning`, `LifeOS.Pdf.Ocr`, `LifeOS.Pdf.Scanning`, `LifeOS.Pdf.Html`, and the
`LifeOS.Pdf.All` meta-package (dependencies only, no code: the seven above in one install).

The version lives in `VersionPrefix` (`Directory.Build.props`) and must match the tag and the
top entry of `CHANGELOG.md`.

## 1. Local release (no GitHub Actions)

Works on any machine with the .NET SDK; nothing else is required.

```powershell
# 1) Post the release in the changelog, then bump VersionPrefix (e.g. 1.1.0) and commit.
# 2) Build the packages (artifacts/*.nupkg):
pwsh tools/release.ps1 -PackOnly

# 3) Publish. The key stays local; it is never printed by the script.
$env:NUGET_API_KEY = 'nuget-api-key'          # scope: "Push new packages and package versions"
pwsh tools/release.ps1
```

The script packs all seven projects with `-p:Version=<VersionPrefix>`, lists what it built, then
pushes with `--skip-duplicate` (re-running is harmless). Useful options:

| Option | Effect |
|---|---|
| `-Version 1.1.0` | override the version instead of reading `VersionPrefix` |
| `-ApiKeyFile C:\temp\nuget.key` | read the key from a file (trimmed), not from the environment |
| `-PackOnly` | build the `.nupkg` files and stop |
| `-AllowDirty` | pack even when the working tree has uncommitted changes |
| `-OutputDir out/packages` | write the packages somewhere else (`artifacts/` by default) |

Then tag the commit and publish the tag:

```bash
git tag -a v1.1.0 -m "LifeOS.Pdf 1.1.0"
git push origin v1.1.0
```

## 2. GitHub Actions (tag-triggered)

Pushing a `v*` tag runs `.github/workflows/ci.yml`: tests on Windows + Linux, the sample on
.NET 5–10 with external validation, then `pack & publish` using NuGet Trusted Publishing
(`NuGet/login@v1`, no API key stored in the repo).

Requirements:

- Actions minutes available (a billing lock stops every job at start with
  *"The job was not started because your account is locked due to a billing issue"*);
- a trusted-publishing policy on nuget.org for `Fajararifk/LifeOS-Pdf` and
  `.github/workflows/ci.yml`, and the `production` environment.

If Actions is unavailable, use the local route.

## Checklist

1. `dotnet build -c Release` and `dotnet test -c Release` are green.
2. `dotnet run -c Release --project samples/LifeOS.Pdf.Samples` and
   `python tools/validate_pdf.py` pass.
3. `CHANGELOG.md` has the version with a date; `VersionPrefix` matches.
4. `pwsh tools/release.ps1 -PackOnly` produced seven `.nupkg` files.
5. Publish, then check `https://www.nuget.org/packages/LifeOS.Pdf/<version>` (indexing takes a
   few minutes).

## Troubleshooting

- **403 on a brand-new package id** (`LifeOS.Pdf.Ocr`, …): the key is scoped to *Push only
  package versions*, which can add versions only to packages that already exist. Create a key
  with *Push new packages and package versions* (nuget.org → Account → API keys). The script
  prints a note before pushing when an id is new; a partially failed run is safe to repeat
  (`--skip-duplicate`).
- **401**: the key is invalid or expired.
- **409 Conflict**: that version already exists. NuGet versions are permanent — bump
  `VersionPrefix`; `--skip-duplicate` makes an accidental re-run a no-op instead.
- **A package is missing from the feed**: the flat-container index
  (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`) updates within a few minutes.
