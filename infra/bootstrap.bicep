targetScope = 'subscription'

@description('Base application name used for tags and generated resource names.')
param applicationName string = 'hablotruck'

@description('Short environment name such as dev or prod.')
param environmentName string

@description('Azure region for the resource group and all child resources.')
param location string

@description('Resource group that will host the HabloTruck environment.')
param resourceGroupName string

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

@secure()
param httpApiKey string

@secure()
param manyChatApiKey string

@secure()
param stripeSecretKey string

@secure()
param stripeWebhookSecret string

param manyChatBaseUrl string = 'https://api.manychat.com'
param manyChatAddTagByNamePath string = 'fb/subscriber/addTagByName'
param manyChatRemoveTagByNamePath string = 'fb/subscriber/removeTagByName'
param manyChatSetCustomFieldByNamePath string = 'fb/subscriber/setCustomFieldByName'
param manyChatSendFlowPath string = 'fb/sending/sendFlow'
param manyChatPaymentFailedFlowNs string = ''
param manyChatPaymentRecoveryReminderFlowNs string = ''
param manyChatRenewalReminderFlowNs string = ''
param manyChatSaveBeforeChurnFlowNs string = ''

param stripeCustomerPortalConfigurationId string = ''
param stripeIndividualMonthlyPriceId string
param stripeIndividualYearlyPriceId string
param stripeFleetSeatMonthlyPriceId string
param stripeCdlCohort25PriceId string = ''
param stripeCdlCohort50PriceId string = ''
param stripeCdlCohort100PriceId string = ''
param stripeCdlEnglishCohortPriceId string = ''
param stripeTestingPriceId string = ''

@description('Optional extra resource tags.')
param tags object = {}

var rgTags = union({
  app: applicationName
  environment: environmentName
  managedBy: 'bicep'
  workload: 'hablotruck-platform'
}, tags)

resource rg 'Microsoft.Resources/resourceGroups@2024-07-01' = {
  name: resourceGroupName
  location: location
  tags: rgTags
}

module environmentDeployment './main.bicep' = {
  name: 'hablotruck-${environmentName}-environment'
  scope: rg
  params: {
    applicationName: applicationName
    environmentName: environmentName
    location: location
    functionAppName: functionAppName
    appServicePlanName: appServicePlanName
    keyVaultName: keyVaultName
    userAssignedIdentityName: userAssignedIdentityName
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    applicationInsightsName: applicationInsightsName
    storageAccountName: storageAccountName
    deploymentContainerName: deploymentContainerName
    functionRuntimeName: functionRuntimeName
    functionRuntimeVersion: functionRuntimeVersion
    functionSkuName: functionSkuName
    functionSkuTier: functionSkuTier
    maximumInstanceCount: maximumInstanceCount
    instanceMemoryMB: instanceMemoryMB
    httpPerInstanceConcurrency: httpPerInstanceConcurrency
    gracePolicyHours: gracePolicyHours
    companyGracePolicyDays: companyGracePolicyDays
    httpApiKey: httpApiKey
    manyChatApiKey: manyChatApiKey
    stripeSecretKey: stripeSecretKey
    stripeWebhookSecret: stripeWebhookSecret
    manyChatBaseUrl: manyChatBaseUrl
    manyChatAddTagByNamePath: manyChatAddTagByNamePath
    manyChatRemoveTagByNamePath: manyChatRemoveTagByNamePath
    manyChatSetCustomFieldByNamePath: manyChatSetCustomFieldByNamePath
    manyChatSendFlowPath: manyChatSendFlowPath
    manyChatPaymentFailedFlowNs: manyChatPaymentFailedFlowNs
    manyChatPaymentRecoveryReminderFlowNs: manyChatPaymentRecoveryReminderFlowNs
    manyChatRenewalReminderFlowNs: manyChatRenewalReminderFlowNs
    manyChatSaveBeforeChurnFlowNs: manyChatSaveBeforeChurnFlowNs
    stripeCustomerPortalConfigurationId: stripeCustomerPortalConfigurationId
    stripeIndividualMonthlyPriceId: stripeIndividualMonthlyPriceId
    stripeIndividualYearlyPriceId: stripeIndividualYearlyPriceId
    stripeFleetSeatMonthlyPriceId: stripeFleetSeatMonthlyPriceId
    stripeCdlCohort25PriceId: stripeCdlCohort25PriceId
    stripeCdlCohort50PriceId: stripeCdlCohort50PriceId
    stripeCdlCohort100PriceId: stripeCdlCohort100PriceId
    stripeCdlEnglishCohortPriceId: stripeCdlEnglishCohortPriceId
    stripeTestingPriceId: stripeTestingPriceId
    tags: tags
  }
}

output resourceGroupName string = rg.name
output functionAppName string = environmentDeployment.outputs.functionAppName
output functionAppResourceId string = environmentDeployment.outputs.functionAppResourceId
output keyVaultName string = environmentDeployment.outputs.keyVaultName
output keyVaultUri string = environmentDeployment.outputs.keyVaultUri
output storageAccountName string = environmentDeployment.outputs.storageAccountName
output staticWebsiteUrl string = environmentDeployment.outputs.staticWebsiteUrl
output checkoutSuccessUrl string = environmentDeployment.outputs.checkoutSuccessUrl
output checkoutCancelUrl string = environmentDeployment.outputs.checkoutCancelUrl
output companyCheckoutSuccessUrl string = environmentDeployment.outputs.companyCheckoutSuccessUrl
output companyCheckoutCancelUrl string = environmentDeployment.outputs.companyCheckoutCancelUrl
output billingReturnUrl string = environmentDeployment.outputs.billingReturnUrl
output applicationInsightsConnectionString string = environmentDeployment.outputs.applicationInsightsConnectionString
