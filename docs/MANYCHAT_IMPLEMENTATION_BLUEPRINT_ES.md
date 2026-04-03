# Blueprint de Implementacion de ManyChat para HabloTruck

Este documento resume, en español y en formato operativo, cómo implementar HabloTruck en ManyChat sin perderse entre los distintos journeys.

Su objetivo es darte una vista práctica de:

- mapa general de flows
- orden recomendado de implementación
- variables, tags y custom fields mínimos
- lógica general de navegación

## Objetivo General

HabloTruck en ManyChat debe construirse como una arquitectura modular, no como un solo flow gigante.

La estructura recomendada es:

1. Un router principal.
2. Flows especializados por journey.
3. Flows de soporte y recuperación.
4. Flows de retención.

## Mapa General de Flows

```text
Flow 0 - Entry Router
|
|-- Quiero acceso completo
|   |-- Router de acceso individual
|       |-- Suscripción mensual o anual
|       |-- Verificar acceso
|       |-- Mi cuenta
|       |-- Facturación / recuperación
|
|-- Soy empresa o escuela
|   |-- Comprar seats
|   |-- Ver mi código
|
|-- Ya tengo código
    |-- Validar código
    |-- Unirme con código
```

## Journeys Principales

### 1. Individual mensual y anual

Documento:

- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`

Objetivo:

- vender plan mensual o anual
- enviar a Stripe
- verificar activación de acceso

### 2. Empresa o escuela + invite code

Documento:

- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`

Objetivo:

- comprar seats
- recuperar el invite code
- permitir que choferes o alumnos entren con ese código

### 3. Billing recovery, renovación y retention

Documento:

- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`

Objetivo:

- resolver pagos fallidos
- manejar recordatorios de renovación
- manejar `Stay with HabloTruck`
- actualizar método de pago
- reintentar cobro

### 4. Entry router y navegación principal

Documento:

- `docs/MANYCHAT_ENTRY_ROUTER_AND_NAVIGATION.md`

Objetivo:

- mandar a cada usuario al camino correcto
- evitar vender de nuevo a alguien que ya tiene acceso
- mandar a billing recovery cuando corresponde

### 5. Arquitectura maestra

Documento:

- `docs/MANYCHAT_MASTER_ARCHITECTURE.md`

Objetivo:

- conectar todos los journeys en una sola visión

## Orden Recomendado de Implementación

## Fase 1 - Base del bot

Implementa primero:

1. `Flow 0 - Entry Router`
2. `Flow - Router de acceso individual`
3. `Flow - Menú principal con acceso`
4. `Flow - Mi cuenta`

Con esto ya tienes la navegación base.

## Fase 2 - Venta individual

Implementa después:

1. `Elegir plan individual`
2. `Recoger datos`
3. `Crear checkout`
4. `Enviar a Stripe`
5. `Verificar acceso`
6. `Acceso activado`
7. `Error al crear checkout`
8. `Pago cancelado o sin respuesta`
9. `Ya pagué pero no recibí respuesta`

Con esto ya tienes ventas B2C.

## Fase 3 - Empresa o escuela

Implementa después:

1. `Comprar seats para empresa o escuela`
2. `Enviar admin a Stripe`
3. `Buscar código activo`
4. `Mostrar código al admin`
5. `Reenviar código`
6. `Unirme con código`
7. `Completar join`
8. errores relacionados

Con esto ya tienes ventas B2B + reparto de acceso por código.

## Fase 4 - Billing y retención

Implementa después:

1. `Pago fallido detectado`
2. `Crear link de actualización`
3. `Enviar a Stripe Billing Portal`
4. `Reintentar cobro`
5. `Verificar estado de recuperación`
6. `Resultado del reintento`
7. `Recordatorio de renovación`
8. `Stay with HabloTruck`
9. errores de billing

Con esto ya tienes recuperación y retención.

## Variables y Custom Fields Mínimos

Estos son los campos mínimos recomendados para arrancar bien:

- `cf_plan_type`
- `cf_phone_e164`
- `cf_checkout_url`
- `cf_checkout_session_id`
- `cf_last_checkout_error`
- `cf_company_name`
- `cf_requested_seats`
- `cf_company_id`
- `cf_entitlement_id`
- `cf_invite_code`
- `cf_last_company_error`
- `cf_actor_user_pk`
- `cf_actor_user_id`
- `cf_subscription_id`
- `cf_last_billing_error`
- `cf_payment_method_update_url`

## Tags y Fields que el Backend Ya Puede Sincronizar

### Acceso

- `HT_ACCESS_FULL`
- `HT_ACCESS_GRACE`
- `HT_ACCESS_BLOCKED`
- `HT_SRC_INDIVIDUAL`
- `HT_SRC_COMPANY`
- `ht_access_mode`
- `ht_grace_ends_utc`
- `ht_company_id`

### Billing recovery

- `HT_BILLING_ACTION_REQUIRED`
- `HT_BILLING_RECOVERED`
- `ht_billing_recovery_status`
- `ht_billing_recovery_subscription_id`
- `ht_billing_recovery_invoice_id`
- `ht_billing_recovery_invoice_status`
- `ht_billing_recovery_updated_utc`
- `ht_billing_recovery_started_utc`

## Keywords Recomendadas

### Entrada

- `info`
- `inicio`
- `menu`

### Pago y acceso

- `ya pagué`
- `mi acceso`
- `pague`
- `pagué`

### Empresa

- `mi código`
- `código`
- `empresa`
- `escuela`

### Billing

- `pago`
- `actualizar pago`
- `actualizar tarjeta`
- `billing`

## Lógica General Recomendada

- Si el usuario ya tiene `HT_ACCESS_FULL`, no lo mandes otra vez a comprar.
- Si el usuario está en billing recovery, mándalo primero a resolver pago.
- Si el usuario ya tiene company access, no lo fuerces a compra individual.
- Si el usuario dice `ya pagué`, revisa acceso antes de reiniciar el journey.
- Si el usuario es admin, dale siempre acceso fácil a `Ver mi código`.
- Si el usuario está en un reminder de retención, usa `Stay with HabloTruck`, no el mismo flow de error de pago.

## Qué Construir Primero en ManyChat

Si quieres salir rápido con una primera versión funcional, yo construiría en este orden:

1. `Entry Router`
2. `Suscripción individual`
3. `Verificar acceso`
4. `Empresa o escuela`
5. `Invite code`
6. `Billing recovery`
7. `Stay with HabloTruck`
8. `Mi cuenta`

## Checklist de Implementación

Antes de decir que ManyChat está listo, verifica:

- router principal creado
- custom fields creados
- tags creados
- endpoint URLs correctos
- `x-api-key` configurado en todos los External Requests
- success/cancel URLs configurados
- keywords importantes creados
- flow `Verificar mi acceso` creado
- flow `Ver mi código` creado
- flow `Actualizar método de pago` creado
- flow `Stay with HabloTruck` creado

## Documentos Relacionados

- `docs/MANYCHAT_ENTRY_ROUTER_AND_NAVIGATION.md`
- `docs/MANYCHAT_INDIVIDUAL_SUBSCRIPTION_FLOWS.md`
- `docs/MANYCHAT_COMPANY_AND_INVITE_FLOWS.md`
- `docs/MANYCHAT_BILLING_RECOVERY_AND_RENEWAL_FLOWS.md`
- `docs/MANYCHAT_MASTER_ARCHITECTURE.md`

## Conclusión

La mejor manera de construir HabloTruck en ManyChat es:

- empezar con un router claro
- vender solo cuando toca
- verificar acceso después de Stripe
- separar empresa, individual y billing
- tratar `Stay with HabloTruck` como un flow propio de retención

Eso te da una base mucho más ordenada, más fácil de operar y más fácil de escalar.
