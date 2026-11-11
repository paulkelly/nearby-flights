# One-time Azure setup for deploying Flight Radar from GitHub Actions (step 8d). Run it yourself, once, after
# `az login`, from the repo root (Windows PowerShell 5.1 or PowerShell 7):
#
#   powershell -ExecutionPolicy Bypass -File infra/setup-azure.ps1
#
# (-ExecutionPolicy Bypass: Windows blocks .ps1 scripts by default; this allows this one run without changing the
# system setting.)
#
# It creates:
#   1. The resource group everything lives in (rg-flightradar, North Europe). Deleting it later deletes everything,
#      and stops every charge: az group delete --name rg-flightradar
#   2. An identity for GitHub Actions (an Entra ID app registration + service principal) with Contributor rights on
#      that resource group only. Not on the subscription: a leaked workflow can't touch anything else.
#   3. A federated credential: Azure trusts tokens that GitHub signs for this repository's "production"
#      environment. That's OIDC: the workflow proves who it is with a short-lived token from GitHub, so no Azure
#      password or key is ever stored in GitHub.
#   4. A $10 monthly budget on the resource group that emails you (infra/budget.bicep).
#
# Safe to run again: each step checks what already exists.

param(
    [string] $ResourceGroup = 'rg-flightradar',
    [string] $Location = 'northeurope',
    [string] $Repository = 'paulkelly/nearby-flights',
    [string] $Environment = 'production'
)

$ErrorActionPreference = 'Stop'

# Runs az and returns its output as one string. az is a native program, so PowerShell doesn't stop on its failures
# by itself. JSON is filtered in PowerShell rather than with az's --query: quoting JMESPath through PowerShell and
# az.cmd is fragile.
function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed (exit code $LASTEXITCODE)" }
    return ($output -join "`n")
}

# az's JSON output as an array of objects. (Assigned first: Windows PowerShell's ConvertFrom-Json emits a JSON
# array as one object rather than item by item.)
function Invoke-AzJson {
    $text = Invoke-Az @args --output json
    if (-not $text) { return ,@() }
    $parsed = $text | ConvertFrom-Json
    return ,@($parsed)
}

$account = Invoke-Az account show --output json | ConvertFrom-Json
Write-Host "Subscription: $($account.name) ($($account.id))"
Write-Host "Tenant:       $($account.tenantId)"
$answer = Read-Host 'Set up Flight Radar deployment in this subscription? (y/n)'
if ($answer -ne 'y') { return }

# New subscriptions must opt in to each Azure service ("resource provider") before using it. Done here, with your
# rights, because the deploy identity only has rights on the resource group, not the subscription.
foreach ($provider in 'Microsoft.App', 'Microsoft.OperationalInsights', 'Microsoft.DBforPostgreSQL', 'Microsoft.Consumption') {
    Write-Host "Registering resource provider $provider (can take a minute)..."
    Invoke-Az provider register --namespace $provider --wait | Out-Null
}

Write-Host "Resource group $ResourceGroup in $Location..."
$group = Invoke-Az group create --name $ResourceGroup --location $Location --output json | ConvertFrom-Json

$appName = 'flightradar-github-deploy'
$app = (Invoke-AzJson ad app list --display-name $appName) | Select-Object -First 1
if (-not $app) {
    Write-Host "App registration $appName..."
    $app = Invoke-Az ad app create --display-name $appName --output json | ConvertFrom-Json
}
$appId = $app.appId

if (-not ((Invoke-AzJson ad sp list --display-name $appName) | Where-Object { $_.appId -eq $appId })) {
    Write-Host 'Service principal...'
    Invoke-Az ad sp create --id $appId | Out-Null
}

# The subject names exactly which workflows may log in: jobs in this repository that use the "production"
# environment. GitHub only lets the main branch use that environment (set in the repository's settings).
# GitHub's tokens name the owner and repository with their numeric IDs as well as their names
# (repo:owner@123/name@456:...). IDs are never reused, so a repository deleted and re-created under the same name by
# someone else can't inherit this trust. They're public, so they're looked up from GitHub's API.
$githubRepo = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository" -Headers @{ 'User-Agent' = 'flightradar-setup' }
$subject = "repo:$($githubRepo.owner.login)@$($githubRepo.owner.id)/$($githubRepo.name)@$($githubRepo.id):environment:$Environment"
$credentialName = "github-$Environment"
$credentials = Invoke-AzJson ad app federated-credential list --id $appId
if (-not ($credentials | Where-Object { $_.subject -eq $subject })) {
    # Passed as a file: PowerShell mangles the quotes in inline JSON given to native programs.
    $credentialFile = New-TemporaryFile
    @{
        name      = $credentialName
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $subject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json | Set-Content -Path $credentialFile -Encoding ascii
    if ($credentials | Where-Object { $_.name -eq $credentialName }) {
        Write-Host "Updating the federated credential to $subject..."
        Invoke-Az ad app federated-credential update --id $appId --federated-credential-id $credentialName --parameters "@$credentialFile" | Out-Null
    }
    else {
        Write-Host "Federated credential for $subject..."
        Invoke-Az ad app federated-credential create --id $appId --parameters "@$credentialFile" | Out-Null
    }
    Remove-Item $credentialFile
}

if (-not (Invoke-AzJson role assignment list --assignee $appId --scope $group.id --role Contributor)) {
    Write-Host 'Contributor role on the resource group (can take a minute to apply)...'
    Invoke-Az role assignment create --assignee $appId --role Contributor --scope $group.id | Out-Null
}

# The budget alert (infra/budget.bicep): deployed here, once, not by every deploy, because a budget's start date is
# fixed when it's created. Checked through the REST API: budgets aren't listed as ordinary resources.
$budgets = Invoke-Az rest --method get --url "https://management.azure.com$($group.id)/providers/Microsoft.Consumption/budgets?api-version=2023-11-01" | ConvertFrom-Json
if (-not ($budgets.value | Where-Object { $_.name -eq 'budget-flightradar' })) {
    $email = Read-Host 'Email address for the budget alerts ($10 a month: at 50%, at 100%, and when forecast to exceed)'
    Write-Host 'Budget...'
    Invoke-Az deployment group create --resource-group $ResourceGroup --template-file "$PSScriptRoot/budget.bicep" `
        --parameters "contactEmail=$email" | Out-Null
}

# The Postgres admin password: 32 random letters and digits (it goes into a connection string), from a
# cryptographic random source. Bytes that would bias the choice towards the first characters are skipped.
# Copied to the clipboard rather than printed, so it never sits in the terminal's scrollback. Only on request: when
# running this again, the GitHub secret usually already holds the right one.
$newPassword = (Read-Host 'Generate a new Postgres admin password? Only for the first run, or to change it (y/n)') -eq 'y'
if ($newPassword) {
    $characters = [char[]] 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $limit = 256 - (256 % $characters.Length)
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $buffer = New-Object byte[] 1
    $password = New-Object System.Text.StringBuilder
    while ($password.Length -lt 32) {
        $random.GetBytes($buffer)
        if ($buffer[0] -lt $limit) { [void] $password.Append($characters[$buffer[0] % $characters.Length]) }
    }
    $random.Dispose()
    Set-Clipboard -Value $password.ToString()
}

Write-Host ''
Write-Host 'Done. In GitHub: Settings > Environments > production, these should be set:' -ForegroundColor Green
Write-Host "  Variable AZURE_CLIENT_ID         = $appId"
Write-Host "  Variable AZURE_TENANT_ID         = $($account.tenantId)"
Write-Host "  Variable AZURE_SUBSCRIPTION_ID   = $($account.id)"
if ($newPassword) {
    Write-Host '  Secret   POSTGRES_ADMIN_PASSWORD = paste now: a new random password is on your clipboard'
    Write-Host '  (Changing it on a running deployment: update the secret, then redeploy.)'
}
Write-Host "(The IDs aren't secrets: they say who to log in as, not how.)"
