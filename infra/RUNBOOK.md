# HabloTruck Azure Runbook

Este documento describe el orden recomendado para dejar funcionando el despliegue con `Bicep + GitHub OIDC`.

## 1. Prerrequisitos en Azure

Antes de correr los workflows, necesitas:

- una suscripción de Azure activa
- permisos para crear recursos en la suscripción o al menos en el resource group destino
- permisos para crear una `App Registration` o un `User Assigned Managed Identity` con federated credentials

## 2. Crear identidad para GitHub OIDC

La forma recomendada es usar una `App Registration` o `Service Principal` con login federado.

Datos que vas a necesitar después en GitHub:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

### Opción recomendada

Crear una app registration dedicada para CI/CD de HabloTruck.

Luego:

1. Dale acceso a la suscripción o al resource group.
2. Asígnale como mínimo un rol que pueda:
   - crear recursos
   - desplegar plantillas ARM/Bicep
   - publicar a la Function App

En práctica, para arrancar:

- `Contributor` sobre la suscripción

Como el `Resource Group` ahora se crea desde `bootstrap.bicep`, el despliegue necesita permiso para crear resource groups.
Si prefieres permisos más cerrados, entonces crea los resource groups manualmente y vuelve a un flujo de despliegue a nivel `resourceGroup`.

## 3. Crear Federated Credential para GitHub

Debes crear una credencial federada para cada workflow/ambiente que vaya a desplegar.

### `dev`

Configúrala para:

- organización/repositorio: tu repo actual
- branch: `dev`
- audience: `api://AzureADTokenExchange`

### `prod`

Configúrala para:

- organización/repositorio: tu repo actual
- branch: `prod`
- audience: `api://AzureADTokenExchange`

Si usas otra rama para producción, cambia eso en la credencial y en el workflow.

## 3.1 Validación de PRs

El repositorio ahora tiene un workflow dedicado para PRs:

- PR hacia `dev`: solo corre tests
- PR hacia `prod`: solo corre tests

No despliega nada durante el PR.
El despliegue queda reservado al merge sobre la rama destino.

## 4. Configurar Secrets en GitHub

### Compartidos

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

`ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_PROD` se usa para enviar alertas administrativas de fallos de pago en producción a `info@rnmglobalsolutions.com`.
`ADMIN_PAYMENT_ALERTS_SENDGRID_API_KEY_DEV` se usa para probar esas mismas alertas en desarrollo.

## 5. Configurar Variables en GitHub

### `dev`

Opcionales:

- `AZURE_RESOURCE_GROUP_DEV`
- `AZURE_LOCATION_DEV`

Si no las defines:

- resource group: `hablotruck-dev-rg`
- location: `Central US`

### `prod`

Recomendadas:

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

Si no defines `AZURE_RESOURCE_GROUP_PROD` y `AZURE_LOCATION_PROD`:

- resource group: `hablotruck-prod-rg`
- location: `Central US`

## 6. Orden recomendado de despliegue

Hazlo así:

1. Configura OIDC.
2. Crea los secrets y variables de `dev`.
3. Crea un PR desde tu feature branch hacia `dev` y deja que corran los tests.
4. Haz merge a `dev`.
5. Verifica que el workflow cree el resource group, despliegue la infraestructura y publique la app.
6. Valida la app en `dev`.
7. Configura los secrets y variables de `prod`.
8. Crea un PR desde `dev` hacia `prod` y deja que corran los tests.
9. Haz merge a `prod`.
10. Valida `prod`.

## 7. Qué crea el workflow

Cuando corre el workflow:

1. compila y publica la Function App
2. hace login con `azure/login@v2`
3. despliega `infra/bootstrap.bicep` a nivel suscripción
4. `bootstrap.bicep` crea o actualiza el resource group
5. `bootstrap.bicep` invoca `infra/main.bicep`
6. crea o actualiza:
   - `Storage Account`
   - `Log Analytics`
   - `Application Insights`
   - `Key Vault`
   - `User Assigned Managed Identity`
   - `Functions Plan`
   - `Function App`
7. sube el paquete a la Function App

## 8. Validaciones mínimas después de `dev`

Después del primer despliegue a `dev`, revisa:

1. Que la Function App exista y esté en estado `Running`.
2. Que el Key Vault tenga estos secretos:
   - `TableConnectionString`
   - `HttpApiKey`
   - `ManyChatApiKey`
   - `StripeSecretKey`
   - `StripeWebhookSecret`
3. Que la Function App tenga referencias `@Microsoft.KeyVault(...)` resueltas.
4. Que `Application Insights` reciba telemetría.
5. Que la app responda en:
   - `/api/health`

## 9. Smoke tests recomendados

Una vez desplegado `dev`, prueba al menos:

- `GET /api/health`
- crear checkout individual
- crear checkout fleet
- webhook de Stripe en ambiente controlado
- obtener invite activo
- join con código

## 10. Qué revisar si algo falla

### Si falla el login de Azure

Revisa:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- federated credential
- branch permitida por esa credencial

### Si falla Bicep

Revisa:

- nombres globales únicos, sobre todo `Key Vault` y `Storage Account`
- permisos del principal sobre el resource group
- valores vacíos obligatorios, por ejemplo secrets o price ids de producción

### Si falla la Function App al iniciar

Revisa:

- que `Key Vault` permita resolver secretos a la identidad administrada
- que `AzureWebJobsStorage` y `TableStorageConnection` estén presentes
- que `Stripe__*` y `ManyChat__ApiKey` tengan valores reales

## 11. Estrategia recomendada por ambiente

### `dev`

- `Flex Consumption`
- configuración lean
- pruebas funcionales frecuentes

### `prod`

- puedes empezar también en `Flex Consumption`
- deja monitoreo activo desde el día 1
- si la carga sube, luego puedes evolucionar el plan sin rehacer la estructura de IaC

## 12. Archivos relacionados

- [main.bicep](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/main.bicep)
- [bootstrap.bicep](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/bootstrap.bicep)
- [dev.bicepparam](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/environments/dev.bicepparam)
- [prod.bicepparam](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/infra/environments/prod.bicepparam)
- [dev_hablotruckplatform-development.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/dev_hablotruckplatform-development.yml)
- [prod_hablotruckplatform-production.yml](/Users/martell/Desktop/Library/Businesses/RNM%20Global%20Solutions%20LLC/HabloTruck/Backend/HabloTruckPlatformAPI/.github/workflows/prod_hablotruckplatform-production.yml)
