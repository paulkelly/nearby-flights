// Everything Flight Radar runs on in Azure (step 8d), in the resource group the deploy targets (rg-flightradar):
//
//   browser ──HTTPS──► web (nginx, scales to zero) ──► api (internal only, exactly 1 replica) ──► Postgres
//
// Bicep describes what should exist, not the steps to get there: deploying it again with a new image tag changes
// only what differs. Deploy (the workflow does this on every green merge to main):
//   az deployment group create --resource-group rg-flightradar --template-file infra/main.bicep \
//     --parameters imageTag=sha-abc1234 postgresAdminPassword=<secret>
// Check it compiles: az bicep build --file infra/main.bicep

@description('Image tag to run, from GitHub Container Registry: sha-<first 7 of the commit>, as CI publishes them.')
param imageTag string = 'latest'

@description('Password of the Postgres admin user, which the API and the migrations log in as.')
@secure()
param postgresAdminPassword string

param location string = resourceGroup().location

var images = 'ghcr.io/paulkelly/nearby-flights'
var postgresUser = 'flightradar'
var postgresDatabase = 'flightradar'

// ---- Logs ----
// Container stdout/stderr (the API's JSON log lines) go here; query them in the portal under the environment's Logs.
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-flightradar'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30 // the free retention period
    // Stop ingesting for the day after 0.1 GB: at most ~3 GB a month, inside the 5 GB monthly free allowance,
    // so a log storm can't turn into a bill. Logs resume at the next daily reset.
    workspaceCapping: { dailyQuotaGb: json('0.1') }
  }
}

// ---- The Container Apps environment ----
// The shared "cluster" the apps run in: one network, one DNS, one log destination. Consumption plan: billed per
// second of CPU and memory used, with a monthly free grant; idle replicas are billed at a much lower rate.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-flightradar'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

// ---- Postgres ----
// Burstable B1ms with 32 GB: what the 12-month free offer covers. Version 18 matches compose.yaml.
resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  // Server names are global (they become <name>.postgres.database.azure.com), so this one gets a suffix that's
  // unique to the resource group but the same on every deploy.
  name: 'psql-flightradar-${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '18'
    administratorLogin: postgresUser
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
    // Reachable over the internet, but only through the firewall rule below, only with TLS (Azure's default
    // require_secure_transport), and only with the password.
    network: { publicNetworkAccess: 'Enabled' }
  }

  resource database 'databases' = {
    name: postgresDatabase
  }

  // The special 0.0.0.0 rule means "any Azure service": connections from Azure's own address ranges, ours among
  // them, but also other Azure customers'. The password and TLS are what really keep them out. A private network
  // (VNet + private endpoint) would close this, at ~$7/month and more setup.
  resource allowAzure 'firewallRules' = {
    name: 'AllowAzureServices'
    properties: {
      startIpAddress: '0.0.0.0'
      endIpAddress: '0.0.0.0'
    }
  }
}

// Shared by the API and the migration job. Ssl Mode=Require: never fall back to an unencrypted connection.
var postgresConnectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${postgresDatabase};Username=${postgresUser};Password=${postgresAdminPassword};Ssl Mode=Require'

// ---- Migrations ----
// A job runs to completion and stops, like the `migrate` service in compose.yaml. The deploy workflow starts it and
// waits for it before updating the API, so the API never runs against a schema that's behind it.
resource migrate 'Microsoft.App/jobs@2024-03-01' = {
  name: 'flightradar-migrate'
  location: location
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 600
      replicaRetryLimit: 0 // a failed migration needs a human, not a retry
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      secrets: [
        { name: 'postgres-connection', value: postgresConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: '${images}/migrate:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ConnectionStrings__Postgres', secretRef: 'postgres-connection' }
          ]
        }
      ]
    }
  }
}

// ---- API ----
resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'flightradar-api'
  location: location
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single' // a deploy replaces the running version rather than running both
      ingress: {
        // Internal: reachable only from inside the environment (nginx), never from the internet. Like compose.yaml,
        // where only web publishes a port.
        external: false
        targetPort: 8080
        transport: 'auto' // HTTP/1.1 and HTTP/2, WebSocket upgrades included
        // Internal callers may use plain HTTP; without this the ingress redirects them to HTTPS.
        allowInsecure: true
      }
      secrets: [
        { name: 'postgres-connection', value: postgresConnectionString }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${images}/api:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'ConnectionStrings__Postgres', secretRef: 'postgres-connection' }
          ]
          // Container Apps ignores the image's Docker HEALTHCHECK and probes the app directly instead.
          probes: [
            {
              type: 'Liveness' // failing: the container is restarted
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 10
            }
            {
              type: 'Readiness' // failing: no traffic is sent to it until it passes
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      // Exactly one: the background poller must always run (minimum 1), and without Redis the cache, poll claims
      // and SignalR groups are per instance, so a second one would split them (maximum 1).
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

// ---- Web (nginx + the built frontend) ----
resource web 'Microsoft.App/containerApps@2024-03-01' = {
  name: 'flightradar-web'
  location: location
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single' // a deploy replaces the running version rather than running both
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${images}/web:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_URL', value: 'http://${api.properties.configuration.ingress.fqdn}' }
          ]
          probes: [
            {
              type: 'Readiness' // nginx serves the page: ready for traffic
              httpGet: { path: '/', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

@description('The app\'s public address.')
output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'

output migrateJobName string = migrate.name
output apiAppName string = api.name
