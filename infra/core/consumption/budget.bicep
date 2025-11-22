@description('The name of the budget')
param budgetName string

@description('The amount in USD for the budget')
param amount int = 5

@description('The resource group ID to scope the budget to')
param resourceGroupId string

@description('The email address to send alerts to')
param contactEmail string

@description('The alert threshold percentage')
param thresholdPercentage int = 80

@description('The start date for the budget (format: YYYY-MM-DD)')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-11-01' = {
  name: budgetName
  scope: resourceGroup()
  properties: {
    category: 'Cost'
    amount: amount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
      endDate: '2099-12-31' // Far future date for ongoing budget
    }
    filter: {}
    notifications: {
      NotificationAtThreshold: {
        enabled: true
        operator: 'GreaterThanOrEqualTo'
        threshold: thresholdPercentage
        contactEmails: [
          contactEmail
        ]
        thresholdType: 'Actual'
      }
    }
  }
}

@description('The ID of the budget')
output id string = budget.id

@description('The name of the budget')
output name string = budget.name
