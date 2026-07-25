using '../bootstrap.bicep'

param applicationName = 'hablotruck'
param environmentName = 'prod'
param location = 'Central US'
param resourceGroupName = 'hablotruck-prod-rg'

param functionAppName = 'HabloTruckPlatform-production'
param appServicePlanName = 'hablotruck-prod-plan'
param keyVaultName = 'hablotruck-prod-kv'
param userAssignedIdentityName = 'hablotruck-prod-kvref-mi'
param logAnalyticsWorkspaceName = 'hablotruck-prod-law'
param applicationInsightsName = 'hablotruck-prod-ai'

param deploymentContainerName = 'function-deployments'
param maximumInstanceCount = 60
param instanceMemoryMB = 2048
param httpPerInstanceConcurrency = 32

param gracePolicyHours = 72
param companyGracePolicyDays = 7

param httpApiKey = ''
param manyChatApiKey = ''
param stripeSecretKey = ''
param stripeWebhookSecret = ''

param adminPaymentAlertsEnabled = true
param adminPaymentAlertsSendGridApiKey = ''
param adminPaymentAlertsToEmail = 'grettadecien@gmail.com'
param adminPaymentAlertsFromEmail = 'info@rnmglobalsolutions.com'
param adminPaymentAlertsFromName = 'HabloTruck Production Alerts'

param manyChatBaseUrl = 'https://api.manychat.com'
param manyChatAddTagByNamePath = 'fb/subscriber/addTagByName'
param manyChatRemoveTagByNamePath = 'fb/subscriber/removeTagByName'
param manyChatSetCustomFieldByNamePath = 'fb/subscriber/setCustomFieldByName'
param manyChatSendFlowPath = 'fb/sending/sendFlow'
param manyChatPaymentFailedFlowNs = ''
param manyChatPaymentRecoveryReminderFlowNs = ''
param manyChatRenewalReminderFlowNs = ''
param manyChatSaveBeforeChurnFlowNs = ''

param stripeCustomerPortalConfigurationId = ''
param stripeIndividualMonthlyPriceId = ''
param stripeIndividualYearlyPriceId = ''
param stripeFleetSeatMonthlyPriceId = ''
param stripeCdlCohort25PriceId = ''
param stripeCdlCohort50PriceId = ''
param stripeCdlCohort100PriceId = ''
param stripeCdlEnglishCohortPriceId = ''
param stripeTestingPriceId = ''
param allowedCheckoutRedirectHosts = []
param enableKeyVaultPurgeProtection = true

param tags = {
  environment: 'prod'
  mode: 'scaled-flex'
}
