# HabloTruck Infrastructure

Esta carpeta contiene la infraestructura de Azure para HabloTruck usando Bicep.

## Qué crea

- `Storage Account`
- `Blob container` interno para despliegues de Flex Consumption
- `Static website` en el mismo Storage Account para páginas de `successUrl` y `cancelUrl`
- `Log Analytics Workspace`
- `Application Insights`
- `User Assigned Managed Identity` para referencias de Key Vault
- `Key Vault`
- `Azure Functions App Service Plan` en `Flex Consumption`
- `Azure Function App`

## Decisiones aplicadas

- `Central US` como región por defecto.
- `Flex Consumption` para `dev`.
- `Key Vault` desde el inicio.
- Los audios no se hospedan en Azure Blob.
- El `blob container` que crea el template es solo para el paquete de despliegue de la Function App.
- El mismo `Storage Account` también hospeda un mini sitio estático para redirects de Stripe (`success`, `cancel`, `company-success`, `company-cancel`, `billing-return`).
- La habilitación del static website se hace desde el workflow de GitHub con Azure CLI antes de publicar los archivos en `$web`.
- `prod` habilita `Key Vault purge protection`; `dev` lo deja desactivado para no endurecer en exceso el ambiente de pruebas.
- Las tablas de Azure Table Storage no se crean aquí porque la aplicación ya las inicializa al arrancar.
- El `Resource Group` ahora se crea desde `bootstrap.bicep`, por lo que el principal de GitHub necesita permisos a nivel suscripción o un alcance equivalente que permita crear resource groups.

## App settings importantes

El template publica los nombres que la solución ya usa hoy:

- `AzureWebJobsStorage`
- `DEPLOYMENT_STORAGE_CONNECTION_STRING`
- `HttpApiKey`
- `APPLICATIONINSIGHTS_CONNECTION_STRING`
- `ManyChat__*`
- `Stripe__*`

También publica ambos nombres de storage para evitar drift entre ambientes:

- `TableStorageConnection`
- `TableConnectionString`

Y publica la allow-list de hosts válidos para redirects de Stripe en `Stripe__AllowedCheckoutRedirectHosts__*`.
Por defecto incluye el host del static website del mismo Storage Account.

## Archivos

- `bootstrap.bicep`: template a nivel suscripción que crea el `Resource Group` y luego llama al módulo principal.
- `main.bicep`: template principal a nivel `resourceGroup`.
- `environments/dev.bicepparam`: configuración base de desarrollo.
- `environments/prod.bicepparam`: configuración base de producción.
- `RUNBOOK.md`: guía operativa para OIDC, GitHub secrets, despliegue y smoke tests.

## Naming final por ambiente

### `dev`

- resource group por defecto: `hablotruck-dev-rg`
- function app: `HabloTruckPlatform-development`
- plan: `hablotruck-dev-plan`
- key vault: `hablotruck-dev-kv`
- managed identity: `hablotruck-dev-kvref-mi`
- log analytics: `hablotruck-dev-law`
- application insights: `hablotruck-dev-ai`

### `prod`

- resource group por defecto: `hablotruck-prod-rg`
- function app: `HabloTruckPlatform-production`
- plan: `hablotruck-prod-plan`
- key vault: `hablotruck-prod-kv`
- managed identity: `hablotruck-prod-kvref-mi`
- log analytics: `hablotruck-prod-law`
- application insights: `hablotruck-prod-ai`

## GitHub Actions

El workflow de `dev` ahora:

1. compila la Function App,
2. despliega un Bicep a nivel suscripción,
3. ese Bicep crea o actualiza el resource group,
4. despliega la infraestructura del ambiente dentro de ese resource group,
5. publica el artefacto a la Function App.
6. publica las páginas estáticas de checkout en el static website del Storage Account.
7. usa `concurrency` para evitar despliegues solapados del mismo ambiente.

El workflow de `prod` sigue la misma idea, pero está orientado a la rama `prod` y usa `environment: production`.
Además, existe un workflow de validación para PRs que solo corre tests y no despliega.
También serializa despliegues con `concurrency` y usa un timeout mayor para evitar quedar colgado indefinidamente.

## Static website y URLs de checkout

El despliegue ahora también produce un sitio estático con estas páginas:

- `success.html`
- `cancel.html`
- `company-success.html`
- `company-cancel.html`
- `billing-return.html`

El Bicep expone outputs para:

- `staticWebsiteUrl`
- `checkoutSuccessUrl`
- `checkoutCancelUrl`
- `companyCheckoutSuccessUrl`
- `companyCheckoutCancelUrl`
- `billingReturnUrl`

Estas URLs son las candidatas naturales para usar como:

- `successUrl`
- `cancelUrl`

en los requests de Stripe checkout y billing portal.

## Ramas asumidas

- `dev` despliega desde la rama `dev`
- `prod` despliega desde la rama `prod`

Si tu rama productiva usa otro nombre, ajusta el trigger del workflow de producción.

## Validación de PR

- PR hacia `dev`: corre solo tests
- PR hacia `prod`: corre solo tests
- El despliegue sucede únicamente después del merge sobre `dev` o `prod`

## Secrets requeridos en GitHub

### Compartidos para OIDC

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

### `dev`

- `HTTP_API_KEY_DEV`
- `MANYCHAT_API_KEY_DEV`
- `STRIPE_SECRET_KEY_DEV`
- `STRIPE_WEBHOOK_SECRET_DEV`
- `ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_DEV`

### `prod`

- `HTTP_API_KEY_PROD`
- `MANYCHAT_API_KEY_PROD`
- `STRIPE_SECRET_KEY_PROD`
- `STRIPE_WEBHOOK_SECRET_PROD`
- `ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_PROD`

`ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_PROD` habilita emails de alerta para fallos de pago en producción.
El destinatario por defecto es `grettadecien@gmail.com`.
`ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_DEV` habilita el mismo flujo en desarrollo para pruebas.

## Variables opcionales en GitHub

### `dev`

- `AZURE_RESOURCE_GROUP_DEV`
- `AZURE_LOCATION_DEV`

Si no las defines, el workflow de `dev` usa:

- resource group: `hablotruck-dev-rg`
- location: `Central US`

### `prod`

- `AZURE_RESOURCE_GROUP_PROD`
- `AZURE_LOCATION_PROD`
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

Si no defines `AZURE_RESOURCE_GROUP_PROD` y `AZURE_LOCATION_PROD`, el workflow de `prod` usa:

- resource group: `hablotruck-prod-rg`
- location: `Central US`

## Recomendación práctica

Para `dev`, ya puedes apoyarte en los valores base del archivo `dev.bicepparam`.

Para `prod`, conviene mantener los price ids y flow namespaces en `GitHub Variables`, no hardcodeados en el repo, porque son datos operativos que cambian entre ambientes aunque no sean secretos.
