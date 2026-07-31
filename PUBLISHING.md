# Publishing MinimalSerializers.Json

Package id: **`MinimalSerializers.Json`**  
Primary path: **Trusted Publishing** via GitHub Actions OIDC (no long-lived NuGet API key).  
Workflow file: **`.github/workflows/release.yml`**  
Workflow file name for nuget.org policy: **`release.yml`** (file name only).

Version comes from the git tag (`v1.0.0` → `1.0.0`).

## Recommended: Trusted Publishing (release workflow)

### 1) Configure nuget.org trusted policy

1. Sign in at [nuget.org](https://www.nuget.org/).
2. Open your username menu → **Trusted Publishing**  
   (direct: <https://www.nuget.org/account/trusted-publishing> if available).
3. **Add a new trusted publishing policy** with:

| Field | Value |
| --- | --- |
| **Repository Owner** | `JohnGalt1717` |
| **Repository** | `MinimalSerializers` |
| **Workflow File** | `release.yml` |
| **Environment** | *(leave empty — this workflow does not use a GitHub Environment)* |

Owner can be your user or an organization that owns the package.

> Enter **`release.yml` only** — do **not** include `.github/workflows/`.

### 2) Set your nuget.org username on the GitHub repo

`NuGet/login` needs your **nuget.org profile username** (not email):

```bash
# replace with your nuget.org profile name
gh variable set NUGET_USER -R JohnGalt1717/MinimalSerializers --body "YOUR_NUGET_ORG_USERNAME"
```

### 3) Tag a release

```bash
git checkout main
git pull
git tag v1.0.1
git push origin v1.0.1
gh run watch --repo JohnGalt1717/MinimalSerializers
```

The `release` workflow will:

1. build + test
2. pack `MinimalSerializers.Json`
3. exchange a GitHub OIDC token for a **temporary** nuget.org API key (`NuGet/login@v1`)
4. `dotnet nuget push`
5. create a GitHub Release with the nupkg/snupkg attached

## Optional: local publish with API key (`.env`)

For emergency/local pushes only:

1. Create a classic API key at <https://www.nuget.org/account/apikeys>
2. `cp .env.example .env` and set `NUGET_API_KEY=...`
3. `./scripts/publish-nuget.sh`

## Verify publish

```bash
open "https://www.nuget.org/packages/MinimalSerializers.Json/"
curl -s https://api.nuget.org/v3-flatcontainer/minimalserializers.json/index.json
```

## Notes

- Trusted Publishing temporary keys last about **1 hour**; login runs immediately before push.
- `--skip-duplicate` makes re-runs safe if that version already exists.
- Symbols ship as `.snupkg` next to the `.nupkg`.
- Private repos may show a temporarily active policy until the first successful publish.
