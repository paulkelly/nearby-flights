// A monthly cost budget on the resource group, with email alerts (step 8d). Deployed once by setup-azure.ps1, not by
// every deploy: a budget's start date is fixed when it's created.
//
// Alerts only send email. They never stop or delete anything: Azure pay-as-you-go has no spending cap. To stop every
// charge, delete the resource group (docs/deploy-azure.md).

@description('Where the alerts go.')
param contactEmail string

@description('Monthly amount, in the billing currency.')
param amount int = 10

@description('First day of the month the budget starts, yyyy-MM-01. Defaults to the current month.')
param startDate string = utcNow('yyyy-MM-01')

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: 'budget-flightradar'
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: { startDate: startDate }
    notifications: {
      // Spent 50%, then 100%: early warning, then "it's happened".
      actualHalf: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      actualAll: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [contactEmail]
      }
      // Azure's forecast says the month will end over budget: the earliest warning of all.
      forecastAll: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [contactEmail]
      }
    }
  }
}
