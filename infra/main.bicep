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

@description('false: everything except the API and web apps, which stay as they are. The deploy workflow runs migrations between a deploy with false and one with true, so new code never meets an old schema.')
param deployApps bool = true

@description('OpenSky API client ID: 4,000 credits a day with an account instead of 400 anonymously. Empty = anonymous.')
param openSkyClientId string = ''

@description('OpenSky API client secret. Set together with openSkyClientId, or neither is used.')
@secure()
param openSkyClientSecret string = ''

@description('Google service account key (the whole JSON file) allowed to send Firebase Cloud Messaging pushes to the Android app (step 9d). Empty = no pushes.')
@secure()
param firebaseServiceAccountJson string = ''

@description('Log level of the background poller (FlightRadar.Api.Watching). Debug adds one "Poller tick: ..." line every 10 s.')
@allowed(['Information', 'Debug'])
param pollerLogLevel string = 'Information'

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

// OpenSky is the fallback when adsb.lol fails. Anonymous, it allows 400 credits a day: with adsb.lol down and one
// viewer, the poller used that up in about an hour. Only used when both halves are set (the API refuses one alone).
var openSkyConfigured = !empty(openSkyClientId) && !empty(openSkyClientSecret)

// Pushes to the Android widget's phones (step 9d). Without the key the API skips pushes; everything else works.
var firebaseConfigured = !empty(firebaseServiceAccountJson)

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
resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
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
      secrets: concat(
        [{ name: 'postgres-connection', value: postgresConnectionString }],
        openSkyConfigured ? [{ name: 'opensky-client-secret', value: openSkyClientSecret }] : [],
        firebaseConfigured ? [{ name: 'firebase-service-account', value: firebaseServiceAccountJson }] : []
      )
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${images}/api:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: concat([
            { name: 'ConnectionStrings__Postgres', secretRef: 'postgres-connection' }
            { name: 'Serilog__MinimumLevel__Override__FlightRadar.Api.Watching', value: pollerLogLevel }
            // Requests reach the API through three proxies: Azure's ingress, nginx, Azure's internal ingress. All
            // have private addresses that Azure assigns (and doesn't publish), so trust the private ranges and read up
            // to 3 entries of X-Forwarded-For: the first public address is the browser. Safe here because the API has
            // internal ingress only: every request has come through our own nginx. (Compose keeps 1: see
            // ForwardedHeadersSettings.)
            { name: 'ForwardedHeaders__KnownNetworks__0', value: '10.0.0.0/8' }
            { name: 'ForwardedHeaders__KnownNetworks__1', value: '172.16.0.0/12' }
            { name: 'ForwardedHeaders__KnownNetworks__2', value: '192.168.0.0/16' }
            { name: 'ForwardedHeaders__KnownNetworks__3', value: '100.64.0.0/10' }
            { name: 'ForwardedHeaders__ForwardLimit', value: '3' }
          ], openSkyConfigured ? [
            { name: 'Providers__OpenSky__ClientId', value: openSkyClientId }
            { name: 'Providers__OpenSky__ClientSecret', secretRef: 'opensky-client-secret' }
          ] : [], firebaseConfigured ? [
            { name: 'Firebase__ServiceAccountJson', secretRef: 'firebase-service-account' }
          ] : [])
          // Container Apps ignores the image's Docker HEALTHCHECK and probes the app directly instead. Its default
          // probe timeout is 1 s, shorter than a first TLS connection to Postgres (~0.9 s seen, more when busy), which
          // failed /health/ready at startup.
          probes: [
            {
              type: 'Startup' // until it passes, the others wait: up to 60 s to start before a restart
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 2
              failureThreshold: 30
              timeoutSeconds: 2
            }
            {
              // Failing 3 times in a row: the container is restarted. /health/live checks no dependencies, so a slow
              // database never restarts a healthy API.
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 10
              timeoutSeconds: 3
            }
            {
              type: 'Readiness' // failing: no traffic is sent to it until it passes
              httpGet: { path: '/health/ready', port: 8080 }
              periodSeconds: 10
              timeoutSeconds: 5 // more than the database check's own 3 s connect timeout
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
resource web 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
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
            // api! = "api exists here": both apps depend on deployApps, so web never exists without it.
            { name: 'API_URL', value: 'http://${api!.properties.configuration.ingress.fqdn}' }
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
      // Always one: scaled to zero, the first visit after ~7 quiet minutes waited ~30 s while a replica started.
      // Costs ~$6 a month (an idle replica's memory is billed at the full rate). minReplicas: 0 to save that.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

@description('The app\'s public address.')
output webUrl string = deployApps ? 'https://${web!.properties.configuration.ingress.fqdn}' : ''

output migrateJobName string = migrate.name
output apiAppName string = 'flightradar-api'
