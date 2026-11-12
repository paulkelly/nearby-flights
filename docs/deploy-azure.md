# Deploying to Azure (step 8d)

Flight Radar runs on Azure Container Apps with Azure Database for PostgreSQL, deployed by GitHub Actions after every
green merge to `main`. This page covers the one-time setup, what it costs, and how to tear it all down.

```
browser ──HTTPS──► web (nginx, always 1 replica) ──► api (internal, always 1 replica) ──► Postgres (B1ms)
```

No Redis: one API instance doesn't need a shared cache, claims or backplane, and the app runs without it.

## What it costs

Estimated in November 2026 (East US prices; North Europe is a few percent higher). Check the
[Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) for current numbers.

| | First 12 months (new account) | After |
|---|---|---|
| API (0.25 vCPU / 0.5 GiB, always on; billed at the idle rate while nobody watches) | ~$4–15 | same |
| Web (always on, mostly idle; `minReplicas: 0` saves this, but the first visit after a quiet spell then waits ~30 s) | ~$6 | same |
| Postgres Flexible Server B1ms, 32 GB | $0 (free offer) | ~$16 |
| Logs (Log Analytics, 5 GB/month free, daily cap) | ~$0 | ~$0 |
| **Total** | **~$10–21/month** | **~$26–37/month** |

A new account's $200 credit covers the first 30 days. Pay-as-you-go has **no spending cap**: the budget alert only
emails you. To stop every charge at once, delete the resource group (see [Tearing it down](#tearing-it-down)).

## One-time setup

### 1. Azure account

Create one at https://azure.microsoft.com/free (a card is needed for identity checks). The 12 months of free services
continue only if you **upgrade to pay-as-you-go within 30 days**, when the credit ends; you're then billed for usage
beyond the free amounts.

### 2. Azure CLI

```powershell
winget install --exact --id Microsoft.AzureCLI
```

Open a new terminal afterwards so `az` is on the PATH, then sign in (a browser window opens):

```powershell
az login
```

### 3. Run the setup script

From the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File infra/setup-azure.ps1
```

(`-ExecutionPolicy Bypass` lets this one run through Windows' default block on scripts, without changing the
system setting.)

It shows which subscription it will use and asks before changing anything. It registers the Azure services we use,
creates the resource group `rg-flightradar` in North Europe, and creates the identity GitHub Actions deploys as:
an app registration with **Contributor on that resource group only**, trusting GitHub's OIDC tokens for this
repository's `production` environment. No Azure password or key is stored in GitHub. (GitHub's tokens name the
repository by owner and repository **ID** as well as name, `repo:paulkelly@1561169/nearby-flights@1443025519:...`, so
a repository re-created under the same name can't inherit the trust; the script looks the IDs up.) It also asks for an email
address and creates a **$10/month budget alert** (`infra/budget.bicep`): emails at 50% and 100% of actual spend, and
when Azure forecasts the month will go over. It's safe to run again.

At the end it prints three IDs and, if you ask for one (first run: yes; later runs: no), puts a new random Postgres
password **on your clipboard** (not on screen).

### 4. GitHub environment

In the repository: **Settings → Environments → New environment** → `production`.

- **Deployment branches and tags**: *Selected branches and tags* → add `main`. Only workflows on `main` can then
  use this environment, and so only they can log in to Azure.
- **Environment secrets** → `POSTGRES_ADMIN_PASSWORD`: paste the password from your clipboard first, before you
  copy anything else.
- **Environment variables**: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` (printed by the
  script).

Optional, in the same environment (the next deploy applies them):

- **OpenSky account**: variable `OPENSKY_CLIENT_ID` and secret `OPENSKY_CLIENT_SECRET` (both, or neither). OpenSky is
  the fallback when adsb.lol fails. Anonymous, it allows 400 credits a day, which the poller uses up in about an hour
  of viewing while adsb.lol is down; an account gets 4,000. Your values are in your user-secrets:
  `dotnet user-secrets list --project src/FlightRadar.Api` (the `Providers:OpenSky:` lines). The deploy log says
  which mode it used.
- **Poller logging**: variable `POLLER_LOG_LEVEL` = `Debug` adds one line per tick to the API's logs
  (`Poller tick: 1 watched, 1 fetched, ...`; ~3 MB a day). Unset or `Information` = off.

The password lives only in GitHub (and, once deployed, in Azure). If it's ever lost, run the script again for a new
one, update the secret, and redeploy.

## How a deploy runs

Every push to `main` that passes CI publishes its images and then runs **Deploy** (`.github/workflows/deploy.yml`):

1. Log in to Azure with GitHub's OIDC token (no stored password or key).
2. Deploy `infra/main.bicep` with `deployApps=false`: everything but the API and web apps, including the migration
   job at the new image tag. Creates it all on the first run; afterwards only what changed.
3. Run the migration job and wait for it. If it fails, the deploy stops and the apps keep running the old version.
4. Deploy `infra/main.bicep` again with `deployApps=true`: the API and web apps move to the new tag.
5. Smoke test: the page loads at the public address, which the run's summary shows (and **Environments →
   production** in the repository links to).

The first deploy takes longest (Postgres alone takes several minutes to create).

### Rolling back

**Actions → Deploy → Run workflow** on `main`, with an older tag such as `sha-abc1234` (CI keeps the newest 10 images
of each). That redeploys those images. The database schema is not rolled back: an older migration bundle finds
nothing it knows to apply and leaves the database alone. So migrations should stay backwards compatible: add
columns and tables; rename or drop them only once no deployed version uses them.

## Checking a deployment

All read-only. In PowerShell, after `az login`:

```powershell
# What's running, at which image tag
az containerapp list -g rg-flightradar --query "[].{name:name, image:properties.template.containers[0].image, min:properties.template.scale.minReplicas}" -o table

# The API's replica: restarts and readiness
az containerapp replica list -n flightradar-api -g rg-flightradar --query "[].{created:properties.createdTime, restarts:properties.containers[0].restartCount, ready:properties.containers[0].ready}" -o table
```

**Logs** are in Log Analytics (portal: the environment `cae-flightradar` → *Logs*), in two tables:
`ContainerAppConsoleLogs_CL` (what the containers print: the API's JSON log lines) and `ContainerAppSystemLogs_CL`
(Azure's own events: image pulls, probe failures, restarts, scaling). From the command line without installing an
extension, through the query API:

```powershell
$ws = az monitor log-analytics workspace show -g rg-flightradar -n log-flightradar --query customerId -o tsv
$body = New-TemporaryFile
@{ query = "ContainerAppSystemLogs_CL | where TimeGenerated > ago(1h) and Reason_s == 'ProbeFailed' | project TimeGenerated, ContainerAppName_s, Log_s" } |
    ConvertTo-Json | Set-Content $body -Encoding ascii
(az rest --method post --url "https://api.loganalytics.azure.com/v1/workspaces/$ws/query" `
    --resource https://api.loganalytics.io --body "@$body" | Out-String | ConvertFrom-Json).tables[0].rows
Remove-Item $body
```

Useful queries (Kusto Query Language; note `first` and `last` are reserved words, so don't use them as column names):

- Upstream fetches per minute (each fetch logs exactly one of these: adsb.lol answered, or it failed and OpenSky was
  tried). With nobody using the app they stop within ~40 s: watches last 30 s, ticks are 10 s apart.
  `ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'flightradar-api' | extend msg = tostring(parse_json(Log_s)['@m']) | where msg has 'adsb.lol returned' or msg has 'trying' | summarize adsbLol = countif(msg has 'adsb.lol returned'), fellBack = countif(msg has 'trying') by bin(TimeGenerated, 1m) | render timechart`
- Android widgets registering (step 9c): `ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'flightradar-api' and Log_s has 'api/devices' | project TimeGenerated, tostring(parse_json(Log_s)['@m'])`. A registered widget's area is then fetched once a minute with nobody viewing (the upstream-fetches query above); removing the widget stops it within a couple of minutes.
- Poller ticks (with `POLLER_LOG_LEVEL` = `Debug`): `ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'flightradar-api' and Log_s has 'Poller tick'`.

- The API's requests and who made them: `ContainerAppConsoleLogs_CL | where ContainerAppName_s == 'flightradar-api' and Log_s has 'responded' | extend j = parse_json(Log_s) | project TimeGenerated, tostring(j['@m']), tostring(j.ClientIp)`.
  `ClientIp` should be the visitor's public address: if it's a private one, forwarded headers aren't trusted right.
- Errors: `ContainerAppConsoleLogs_CL | where Log_s has '"@l":"Error"'`.
- Events by kind: `ContainerAppSystemLogs_CL | summarize n = count() by ContainerAppName_s, Reason_s`.

From outside, like a browser: the page, `/api/owners/me` (401 without a token: proves nginx reaches the API), and
`/api/flights?lat=53.42&lon=-6.27&radiusKm=10`.

## Tearing it down

```powershell
az group delete --name rg-flightradar
```

This deletes everything the deployment created and stops all charges. The GitHub identity (app registration
`flightradar-github-deploy`) costs nothing; remove it too with `az ad app delete --id <AZURE_CLIENT_ID>`.
