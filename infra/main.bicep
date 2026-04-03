targetScope = 'resourceGroup'

@description('Base application name used for tags and generated resource names.')
param applicationName string = 'hablotruck'

@description('Short environment name such as dev or prod.')
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Azure Function App name.')
param functionAppName string

@description('App Service plan name.')
param appServicePlanName string

@description('Azure Key Vault name.')
param keyVaultName string

@description('User-assigned managed identity used for Key Vault references.')
param userAssignedIdentityName string

@description('Log Analytics workspace name.')
param logAnalyticsWorkspaceName string

@description('Application Insights name.')
param applicationInsightsName string

@description('Storage account name. Leave empty to generate a unique name.')
@minLength(0)
@maxLength(24)
param storageAccountName string = ''

@description('Blob container used internally by Flex Consumption deployments.')
param deploymentContainerName string = 'function-deployments'

@description('Function runtime name.')
@allowed([
  'dotnet-isolated'
])
param functionRuntimeName string = 'dotnet-isolated'

@description('Function runtime version.')
param functionRuntimeVersion string = '10.0'

@description('App Service plan SKU name. FC1 keeps the app on Flex Consumption.')
param functionSkuName string = 'FC1'

@description('App Service plan SKU tier.')
param functionSkuTier string = 'FlexConsumption'

@description('Maximum number of Flex Consumption instances.')
@minValue(1)
param maximumInstanceCount int = 20

@description('Memory allocated to each Flex Consumption instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('HTTP concurrency per instance.')
@minValue(1)
param httpPerInstanceConcurrency int = 16

@description('Grace policy hours.')
@minValue(1)
param gracePolicyHours int = 72

@description('Company grace policy days.')
@minValue(1)
param companyGracePolicyDays int = 7

@description('HTTP API key expected by the backend.')
@secure()
param httpApiKey string

@description('ManyChat API key.')
@secure()
param manyChatApiKey string

@description('Stripe secret key.')
@secure()
param stripeSecretKey string

@description('Stripe webhook secret.')
@secure()
param stripeWebhookSecret string

@description('ManyChat base URL.')
param manyChatBaseUrl string = 'https://api.manychat.com'

@description('ManyChat add tag path.')
param manyChatAddTagByNamePath string = 'fb/subscriber/addTagByName'

@description('ManyChat remove tag path.')
param manyChatRemoveTagByNamePath string = 'fb/subscriber/removeTagByName'

@description('ManyChat set custom field path.')
param manyChatSetCustomFieldByNamePath string = 'fb/subscriber/setCustomFieldByName'

@description('ManyChat send flow path.')
param manyChatSendFlowPath string = 'fb/sending/sendFlow'

@description('Optional ManyChat payment failed flow namespace.')
param manyChatPaymentFailedFlowNs string = ''

@description('Optional ManyChat payment recovery flow namespace.')
param manyChatPaymentRecoveryReminderFlowNs string = ''

@description('Optional ManyChat renewal reminder flow namespace.')
param manyChatRenewalReminderFlowNs string = ''

@description('Optional ManyChat save-before-churn flow namespace.')
param manyChatSaveBeforeChurnFlowNs string = ''

@description('Optional Stripe customer portal configuration id.')
param stripeCustomerPortalConfigurationId string = ''

@description('Stripe monthly individual price id.')
param stripeIndividualMonthlyPriceId string

@description('Stripe yearly individual price id.')
param stripeIndividualYearlyPriceId string

@description('Stripe monthly fleet seat price id.')
param stripeFleetSeatMonthlyPriceId string

@description('Optional Stripe CDL cohort 25 price id.')
param stripeCdlCohort25PriceId string = ''

@description('Optional Stripe CDL cohort 50 price id.')
param stripeCdlCohort50PriceId string = ''

@description('Optional Stripe CDL cohort 100 price id.')
param stripeCdlCohort100PriceId string = ''

@description('Optional Stripe CDL English cohort price id.')
param stripeCdlEnglishCohortPriceId string = ''

@description('Optional Stripe testing price id.')
param stripeTestingPriceId string = ''

@description('Optional extra resource tags.')
param tags object = {}

var normalizedApp = toLower(replace(applicationName, '-', ''))
var generatedStorageAccountName = take('${normalizedApp}${environmentName}${uniqueString(resourceGroup().id, applicationName, environmentName)}', 24)
var resolvedStorageAccountName = empty(storageAccountName) ? generatedStorageAccountName : toLower(storageAccountName)
var tenantId = subscription().tenantId
var storageAccountKey = storage.listKeys().keys[0].value
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storageAccountKey};EndpointSuffix=${environment().suffixes.storage}'
var commonTags = union({
  app: applicationName
  environment: environmentName
  managedBy: 'bicep'
  workload: 'hablotruck-platform'
}, tags)

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: resolvedStorageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  tags: commonTags
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: deploymentContainerName
  properties: {
    publicAccess: 'None'
  }
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  tags: commonTags
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: applicationInsightsName
  location: location
  kind: 'web'
  tags: commonTags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

resource keyVaultIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: userAssignedIdentityName
  location: location
  tags: commonTags
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: commonTags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    accessPolicies: [
      {
        tenantId: tenantId
        objectId: keyVaultIdentity.properties.principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
    enableRbacAuthorization: false
    enabledForDeployment: false
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource tableConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'TableConnectionString'
  properties: {
    value: storageConnectionString
  }
}

resource httpApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'HttpApiKey'
  properties: {
    value: httpApiKey
  }
}

resource manyChatApiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'ManyChatApiKey'
  properties: {
    value: manyChatApiKey
  }
}

resource stripeSecretKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'StripeSecretKey'
  properties: {
    value: stripeSecretKey
  }
}

resource stripeWebhookSecretSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'StripeWebhookSecret'
  properties: {
    value: stripeWebhookSecret
  }
}

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: appServicePlanName
  location: location
  kind: 'functionapp'
  sku: {
    name: functionSkuName
    tier: functionSkuTier
  }
  tags: commonTags
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: {
      '${keyVaultIdentity.id}': {}
    }
  }
  tags: commonTags
  properties: {
    enabled: true
    httpsOnly: true
    keyVaultReferenceIdentity: keyVaultIdentity.id
    publicNetworkAccess: 'Enabled'
    reserved: true
    serverFarmId: appServicePlan.id
    siteConfig: {
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'AzureWebJobsStorage'
          value: '@Microsoft.KeyVault(SecretUri=${tableConnectionStringSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          value: '@Microsoft.KeyVault(SecretUri=${tableConnectionStringSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'TableStorageConnection'
          value: '@Microsoft.KeyVault(SecretUri=${tableConnectionStringSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'TableConnectionString'
          value: '@Microsoft.KeyVault(SecretUri=${tableConnectionStringSecret.properties.secretUriWithVersion})'
        }
        {
          name: 'HttpApiKey'
          value: '@Microsoft.KeyVault(SecretUri=${httpApiKeySecret.properties.secretUriWithVersion})'
        }
        {
          name: 'GracePolicy__Hours'
          value: string(gracePolicyHours)
        }
        {
          name: 'CompanyGracePolicy__Days'
          value: string(companyGracePolicyDays)
        }
        {
          name: 'ManyChat__AddTagByNamePath'
          value: manyChatAddTagByNamePath
        }
        {
          name: 'ManyChat__ApiKey'
          value: '@Microsoft.KeyVault(SecretUri=${manyChatApiKeySecret.properties.secretUriWithVersion})'
        }
        {
          name: 'ManyChat__BaseUrl'
          value: manyChatBaseUrl
        }
        {
          name: 'ManyChat__PaymentFailedFlowNs'
          value: manyChatPaymentFailedFlowNs
        }
        {
          name: 'ManyChat__PaymentRecoveryReminderFlowNs'
          value: manyChatPaymentRecoveryReminderFlowNs
        }
        {
          name: 'ManyChat__RemoveTagByNamePath'
          value: manyChatRemoveTagByNamePath
        }
        {
          name: 'ManyChat__RenewalReminderFlowNs'
          value: manyChatRenewalReminderFlowNs
        }
        {
          name: 'ManyChat__SaveBeforeChurnFlowNs'
          value: manyChatSaveBeforeChurnFlowNs
        }
        {
          name: 'ManyChat__SendFlowPath'
          value: manyChatSendFlowPath
        }
        {
          name: 'ManyChat__SetCustomFieldByNamePath'
          value: manyChatSetCustomFieldByNamePath
        }
        {
          name: 'Stripe__CdlCohort100PriceId'
          value: stripeCdlCohort100PriceId
        }
        {
          name: 'Stripe__CdlCohort25PriceId'
          value: stripeCdlCohort25PriceId
        }
        {
          name: 'Stripe__CdlCohort50PriceId'
          value: stripeCdlCohort50PriceId
        }
        {
          name: 'Stripe__CdlEnglishCohortPriceId'
          value: stripeCdlEnglishCohortPriceId
        }
        {
          name: 'Stripe__CustomerPortalConfigurationId'
          value: stripeCustomerPortalConfigurationId
        }
        {
          name: 'Stripe__FleetSeatMonthlyPriceId'
          value: stripeFleetSeatMonthlyPriceId
        }
        {
          name: 'Stripe__IndividualMonthlyPriceId'
          value: stripeIndividualMonthlyPriceId
        }
        {
          name: 'Stripe__IndividualYearlyPriceId'
          value: stripeIndividualYearlyPriceId
        }
        {
          name: 'Stripe__StripeSecretKey'
          value: '@Microsoft.KeyVault(SecretUri=${stripeSecretKeySecret.properties.secretUriWithVersion})'
        }
        {
          name: 'Stripe__TestingPriceId'
          value: stripeTestingPriceId
        }
        {
          name: 'Stripe__WebhookSecret'
          value: '@Microsoft.KeyVault(SecretUri=${stripeWebhookSecretSecret.properties.secretUriWithVersion})'
        }
      ]
      ftpsState: 'Disabled'
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
    }
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer.name}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'DEPLOYMENT_STORAGE_CONNECTION_STRING'
          }
        }
      }
      runtime: {
        name: functionRuntimeName
        version: functionRuntimeVersion
      }
      scaleAndConcurrency: {
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
        triggers: {
          http: {
            perInstanceConcurrency: httpPerInstanceConcurrency
          }
        }
      }
    }
  }
}

output functionAppName string = functionApp.name
output functionAppResourceId string = functionApp.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output storageAccountName string = storage.name
output staticWebsiteUrl string = storage.properties.primaryEndpoints.web
output checkoutSuccessUrl string = '${storage.properties.primaryEndpoints.web}success.html'
output checkoutCancelUrl string = '${storage.properties.primaryEndpoints.web}cancel.html'
output companyCheckoutSuccessUrl string = '${storage.properties.primaryEndpoints.web}company-success.html'
output companyCheckoutCancelUrl string = '${storage.properties.primaryEndpoints.web}company-cancel.html'
output billingReturnUrl string = '${storage.properties.primaryEndpoints.web}billing-return.html'
output applicationInsightsConnectionString string = appInsights.properties.ConnectionString
