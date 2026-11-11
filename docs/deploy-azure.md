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
repository's `production` environment. No Azure password or key is stored in GitHub. It also asks for an email
address and creates a **$10/month budget alert** (`infra/budget.bicep`): emails at 50% and 100% of actual spend, and
when Azure forecasts the month will go over. It's safe to run again.

At the end it prints three IDs and puts a new random Postgres password **on your clipboard** (not on screen).

### 4. GitHub environment

In the repository: **Settings → Environments → New environment** → `production`.

- **Deployment branches and tags**: *Selected branches and tags* → add `main`. Only workflows on `main` can then
  use this environment, and so only they can log in to Azure.
- **Environment secrets** → `POSTGRES_ADMIN_PASSWORD`: paste the password from your clipboard first, before you
  copy anything else.
- **Environment variables**: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID` and `AZURE_SUBSCRIPTION_ID` (printed by the
  script).

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

## Tearing it down

```powershell
az group delete --name rg-flightradar
```

This deletes everything the deployment created and stops all charges. The GitHub identity (app registration
`flightradar-github-deploy`) costs nothing; remove it too with `az ad app delete --id <AZURE_CLIENT_ID>`.
