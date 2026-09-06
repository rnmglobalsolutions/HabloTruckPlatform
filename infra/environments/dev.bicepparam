using '../bootstrap.bicep'

param applicationName = 'hablotruck'
param environmentName = 'dev'
param location = 'Central US'
param resourceGroupName = 'hablotruck-dev-rg'

param functionAppName = 'HabloTruckPlatform-development'
param appServicePlanName = 'hablotruck-dev-plan'
param keyVaultName = 'hablotruck-dev-kv-rnmgs'
param userAssignedIdentityName = 'hablotruck-dev-kvref-mi'
param logAnalyticsWorkspaceName = 'hablotruck-dev-law'
param applicationInsightsName = 'hablotruck-dev-ai'

param deploymentContainerName = 'function-deployments'
param maximumInstanceCount = 20
param instanceMemoryMB = 2048
param httpPerInstanceConcurrency = 16

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
param adminPaymentAlertsFromName = 'HabloTruck Development Alerts'

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
param stripeIndividualMonthlyPriceId = 'price_1U4EDyLkH66TmATPOhkJnpqt'
param stripeIndividualYearlyPriceId = 'price_1U4EDyLkH66TmATPMHLhlTd0'
param stripeFleetSeatMonthlyPriceId = 'price_1TBMGSLkH66TmATPQyx42A2k'
param stripeCdlCohort25PriceId = ''
param stripeCdlCohort50PriceId = ''
param stripeCdlCohort100PriceId = ''
param stripeCdlEnglishCohortPriceId = ''
param stripeTestingPriceId = 'price_1T9I5wLkH66TmATPaVHICsmU'
param allowedCheckoutRedirectHosts = []
param enableKeyVaultPurgeProtection = false

param tags = {
  environment: 'dev'
  mode: 'lean'
}
