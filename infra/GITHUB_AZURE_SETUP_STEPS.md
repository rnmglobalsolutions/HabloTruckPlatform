# GitHub and Azure Setup Steps

This document explains, step by step, what you need to configure in Azure and GitHub for the current HabloTruck deployment flow.

## Deployment flow

- `feature/* -> PR to dev`: runs tests only
- `merge to dev`: deploys to `dev`
- `dev -> PR to prod`: runs tests only
- `merge to prod`: deploys to `prod`

Related files:

- [pr_hablotruckplatform_validation.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/pr_hablotruckplatform_validation.yml)
- [dev_hablotruckplatform-development.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/dev_hablotruckplatform-development.yml)
- [prod_hablotruckplatform-production.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/prod_hablotruckplatform-production.yml)
- [bootstrap.bicep](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/bootstrap.bicep)
- [main.bicep](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/main.bicep)

## 1. Configure Azure

1. Go to `Microsoft Entra ID > App registrations > New registration`.
2. Create a new app registration.
   Suggested name: `hablotruck-github-actions`
3. Save these values:
   - `Application (client) ID`
   - `Directory (tenant) ID`
   - `Subscription ID`

## 2. Grant Azure permissions

Because the current Bicep setup creates the `Resource Group`, GitHub needs permissions at the **subscription** scope.

1. Go to `Subscriptions > your subscription > Access control (IAM)`.
2. Click `Add role assignment`.
3. Assign this role to the app registration:
   - `Contributor`

This allows the workflow to:

- create the `Resource Group`
- deploy Bicep
- create Azure resources
- deploy the Function App

## 3. Create federated credentials for GitHub OIDC

Go to:

- `Microsoft Entra ID > App registrations > your app > Certificates & secrets > Federated credentials`

Create one credential for `dev`:

- scenario: `GitHub Actions deploying Azure resources`
- repository: this repo
- branch: `dev`
- audience: `api://AzureADTokenExchange`

Create another credential for `prod`:

- same repository
- branch: `prod`
- audience: `api://AzureADTokenExchange`

## 4. Make sure your branches exist in GitHub

You should have:

- `dev`
- `prod`

Expected branch flow:

- `feature/* -> dev`
- `dev -> prod`

## 5. Configure GitHub repository secrets

Go to:

- `Repository > Settings > Secrets and variables > Actions`

Create these repository secrets:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Create these repository secrets for `dev`:

- `HTTP_API_KEY_DEV`
- `MANYCHAT_API_KEY_DEV`
- `STRIPE_SECRET_KEY_DEV`
- `STRIPE_WEBHOOK_SECRET_DEV`

Create these repository secrets for `prod`:

- `HTTP_API_KEY_PROD`
- `MANYCHAT_API_KEY_PROD`
- `STRIPE_SECRET_KEY_PROD`
- `STRIPE_WEBHOOK_SECRET_PROD`

## 6. Configure GitHub repository variables for dev

In the same GitHub section, create these repository variables:

- `AZURE_RESOURCE_GROUP_DEV = hablotruck-dev-rg`
- `AZURE_LOCATION_DEV = Central US`

## 7. Create the production environment in GitHub

Go to:

- `Repository > Settings > Environments > New environment`

Create:

- `production`

Inside the `production` environment, add these environment variables:

- `AZURE_RESOURCE_GROUP_PROD = hablotruck-prod-rg`
- `AZURE_LOCATION_PROD = Central US`
- `STRIPE_INDIVIDUAL_MONTHLY_PRICE_ID_PROD`
- `STRIPE_INDIVIDUAL_YEARLY_PRICE_ID_PROD`
- `STRIPE_FLEET_SEAT_MONTHLY_PRICE_ID_PROD`
- `STRIPE_CUSTOMER_PORTAL_CONFIGURATION_ID_PROD`
- `STRIPE_CDL_COHORT_25_PRICE_ID_PROD`
- `STRIPE_CDL_COHORT_50_PRICE_ID_PROD`
- `STRIPE_CDL_COHORT_100_PRICE_ID_PROD`
- `STRIPE_CDL_ENGLISH_COHORT_PRICE_ID_PROD`
- `STRIPE_TESTING_PRICE_ID_PROD`
- `MANYCHAT_PAYMENT_FAILED_FLOW_NS_PROD`
- `MANYCHAT_PAYMENT_RECOVERY_REMINDER_FLOW_NS_PROD`
- `MANYCHAT_RENEWAL_REMINDER_FLOW_NS_PROD`
- `MANYCHAT_SAVE_BEFORE_CHURN_FLOW_NS_PROD`

Recommended environment protections:

- required reviewers
- deployment branch restriction for `prod`

## 8. Configure branch protection rules

Go to:

- `Repository > Settings > Branches > Add rule`

For `dev`:

- branch name pattern: `dev`
- enable `Require a pull request before merging`
- enable `Require status checks to pass before merging`
- require the PR validation workflow check
- enable `Require conversation resolution before merging`

For `prod`:

- branch name pattern: `prod`
- enable the same protections
- block direct pushes if possible

## 9. Understand what each workflow does

### PR validation

File:

- [pr_hablotruckplatform_validation.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/pr_hablotruckplatform_validation.yml)

Runs on:

- PR to `dev`
- PR to `prod`

Does:

- `dotnet restore`
- `dotnet test`

It does **not** deploy.

### Dev deployment

File:

- [dev_hablotruckplatform-development.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/dev_hablotruckplatform-development.yml)

Runs on:

- push to `dev`

Does:

- OIDC login to Azure
- subscription-scope Bicep deployment
- creates the `dev` resource group if needed
- creates all Azure resources
- deploys the Function App

### Prod deployment

File:

- [prod_hablotruckplatform-production.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/prod_hablotruckplatform-production.yml)

Runs on:

- push to `prod`

Does:

- OIDC login to Azure
- subscription-scope Bicep deployment
- creates the `prod` resource group if needed
- creates all Azure resources
- deploys the Function App

## 10. Resources created by Bicep

The current infrastructure creates:

- `Resource Group`
- `Storage Account`
- `Function App`
- `Functions Plan`
- `Application Insights`
- `Log Analytics`
- `Key Vault`
- `Managed Identity`
- `App Settings`

## 11. Recommended first deployment sequence

1. Configure Azure OIDC.
2. Add GitHub secrets and variables for `dev`.
3. Open a PR from a feature branch into `dev`.
4. Confirm that only tests run.
5. Merge into `dev`.
6. Confirm that Azure resources are created in `dev`.
7. Validate the app with `/api/health`.
8. Configure the remaining `prod` variables and secrets.
9. Open a PR from `dev` into `prod`.
10. Confirm that only tests run.
11. Merge into `prod`.
12. Confirm the `prod` deployment.

## 12. Expected resource group names

- `dev`: `hablotruck-dev-rg`
- `prod`: `hablotruck-prod-rg`

## 13. Useful references

- [RUNBOOK.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/RUNBOOK.md)
- [README.md](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/README.md)
- [dev.bicepparam](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/environments/dev.bicepparam)
- [prod.bicepparam](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/environments/prod.bicepparam)

## Official sources

- GitHub OIDC with Azure: https://docs.github.com/en/actions/security-for-github-actions/security-hardening-your-deployments/configuring-openid-connect-in-azure
- GitHub environments: https://docs.github.com/en/actions/reference/environments
- Protected branches and required checks: https://docs.github.com/articles/about-required-status-checks
- Azure subscription deployments: https://learn.microsoft.com/en-us/cli/azure/deployment/sub?view=azure-cli-lts
