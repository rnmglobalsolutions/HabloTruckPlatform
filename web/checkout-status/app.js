const translations = {
  en: {
    languageLabel: "Language",
    languageEnglish: "English",
    languageSpanish: "Spanish",
    indexTitle: "HabloTruck Checkout Status",
    indexEyebrow: "HabloTruck",
    indexHeading: "Checkout status pages",
    indexLead:
      "This static site hosts the success and cancel pages used by Stripe Checkout and billing recovery flows.",
    indexIndividualSuccess: "Individual success",
    indexIndividualCancel: "Individual cancel",
    indexCompanySuccess: "Company success",
    indexCompanyCancel: "Company cancel",
    indexBillingReturn: "Billing return",
    successTitle: "HabloTruck | Payment Received",
    successChip: "Payment received",
    successHeading: "Your payment was received.",
    successLead:
      "Your HabloTruck subscription is now being confirmed. Return to ManyChat and tap Ya pague or Verificar mi acceso.",
    successButton: "Back to status home",
    successNotesHeading: "What happens next",
    successNote1: "Stripe sends the checkout event to the HabloTruck backend.",
    successNote2: "The backend projects your subscription and access.",
    successNote3: "ManyChat receives the updated access state.",
    successNote4: "You verify access from the chat and continue into HabloTruck.",
    cancelTitle: "HabloTruck | Payment Not Completed",
    cancelChip: "Payment not completed",
    cancelHeading: "Your checkout was not completed.",
    cancelLead:
      "No worries. Return to ManyChat and try again whenever you are ready.",
    cancelButton: "Back to status home",
    cancelNotesHeading: "Recommended next step",
    cancelNote1: "Go back to the HabloTruck chat.",
    cancelNote2: "Choose your plan again.",
    cancelNote3:
      "If the payment page did not load correctly, try one more time before contacting support.",
    companySuccessTitle: "HabloTruck | Company Checkout Received",
    companySuccessChip: "Company checkout received",
    companySuccessHeading: "Your company purchase was received.",
    companySuccessLead:
      "Return to ManyChat to retrieve the invite code for your team. The backend will create your entitlement and active invite automatically.",
    companySuccessButton: "Back to status home",
    companySuccessNotesHeading: "What happens next",
    companySuccessNote1: "Stripe sends the checkout event to the backend.",
    companySuccessNote2:
      "HabloTruck creates or updates the company and entitlement.",
    companySuccessNote3:
      "An active invite code is generated for the purchased seats.",
    companySuccessNote4: "You can retrieve that code from ManyChat.",
    companyCancelTitle: "HabloTruck | Company Checkout Not Completed",
    companyCancelChip: "Checkout not completed",
    companyCancelHeading: "Your company checkout was not completed.",
    companyCancelLead:
      "Return to ManyChat to try again or review your purchase details.",
    companyCancelButton: "Back to status home",
    companyCancelNotesHeading: "Recommended next step",
    companyCancelNote1: "Return to the HabloTruck admin flow in ManyChat.",
    companyCancelNote2:
      "Re-open the checkout link if you still want to purchase seats.",
    companyCancelNote3: "If the same issue repeats, contact support.",
    billingTitle: "HabloTruck | Billing Update Return",
    billingChip: "Billing step completed",
    billingHeading: "Your billing update is being confirmed.",
    billingLead:
      "Return to ManyChat and tap Ya actualice mi metodo, Intentar cobro otra vez, or Verificar estado.",
    billingButton: "Back to status home",
    billingNotesHeading: "What happens next",
    billingNote1: "Stripe confirms the billing portal changes.",
    billingNote2:
      "The HabloTruck backend receives new subscription or invoice updates by webhook.",
    billingNote3: "ManyChat receives updated billing recovery state.",
    notFoundTitle: "HabloTruck | Page Not Found",
    notFoundChip: "Page not found",
    notFoundHeading: "We could not find that page.",
    notFoundLead:
      "Use the status home page or go back to the HabloTruck chat to continue.",
    notFoundButton: "Go to status home",
  },
  es: {
    languageLabel: "Idioma",
    languageEnglish: "Inglés",
    languageSpanish: "Español",
    indexTitle: "HabloTruck Estado del Pago",
    indexEyebrow: "HabloTruck",
    indexHeading: "Páginas de estado del pago",
    indexLead:
      "Este sitio estático aloja las páginas de éxito y cancelación usadas por Stripe Checkout y los flujos de recuperación de cobro.",
    indexIndividualSuccess: "Éxito individual",
    indexIndividualCancel: "Cancelación individual",
    indexCompanySuccess: "Éxito empresa",
    indexCompanyCancel: "Cancelación empresa",
    indexBillingReturn: "Regreso de facturación",
    successTitle: "HabloTruck | Pago Recibido",
    successChip: "Pago recibido",
    successHeading: "Tu pago fue recibido.",
    successLead:
      "Tu suscripción de HabloTruck se está confirmando ahora mismo. Regresa a ManyChat y toca Ya pagué o Verificar mi acceso.",
    successButton: "Volver al inicio",
    successNotesHeading: "Qué pasa ahora",
    successNote1:
      "Stripe envía el evento del checkout al backend de HabloTruck.",
    successNote2: "El backend proyecta tu suscripción y tu acceso.",
    successNote3: "ManyChat recibe el estado actualizado de acceso.",
    successNote4:
      "Verificas tu acceso desde el chat y continúas dentro de HabloTruck.",
    cancelTitle: "HabloTruck | Pago No Completado",
    cancelChip: "Pago no completado",
    cancelHeading: "Tu checkout no se completó.",
    cancelLead:
      "No te preocupes. Regresa a ManyChat e inténtalo de nuevo cuando quieras.",
    cancelButton: "Volver al inicio",
    cancelNotesHeading: "Siguiente paso recomendado",
    cancelNote1: "Regresa al chat de HabloTruck.",
    cancelNote2: "Escoge tu plan otra vez.",
    cancelNote3:
      "Si la página de pago no cargó correctamente, intenta una vez más antes de contactar soporte.",
    companySuccessTitle: "HabloTruck | Compra de Empresa Recibida",
    companySuccessChip: "Compra de empresa recibida",
    companySuccessHeading: "La compra de tu empresa fue recibida.",
    companySuccessLead:
      "Regresa a ManyChat para obtener el código de invitación para tu equipo. El backend creará tu entitlement y el invite activo automáticamente.",
    companySuccessButton: "Volver al inicio",
    companySuccessNotesHeading: "Qué pasa ahora",
    companySuccessNote1: "Stripe envía el evento del checkout al backend.",
    companySuccessNote2:
      "HabloTruck crea o actualiza la empresa y el entitlement.",
    companySuccessNote3:
      "Se genera un código de invitación activo para los asientos comprados.",
    companySuccessNote4: "Puedes recuperar ese código desde ManyChat.",
    companyCancelTitle: "HabloTruck | Compra de Empresa No Completada",
    companyCancelChip: "Checkout no completado",
    companyCancelHeading: "El checkout de tu empresa no se completó.",
    companyCancelLead:
      "Regresa a ManyChat para intentarlo otra vez o revisar los detalles de la compra.",
    companyCancelButton: "Volver al inicio",
    companyCancelNotesHeading: "Siguiente paso recomendado",
    companyCancelNote1:
      "Regresa al flujo de administrador de HabloTruck en ManyChat.",
    companyCancelNote2:
      "Abre de nuevo el enlace de checkout si todavía quieres comprar asientos.",
    companyCancelNote3: "Si el mismo problema se repite, contacta soporte.",
    billingTitle: "HabloTruck | Regreso de Actualización de Facturación",
    billingChip: "Paso de facturación completado",
    billingHeading: "Tu actualización de facturación se está confirmando.",
    billingLead:
      "Regresa a ManyChat y toca Ya actualicé mi método, Intentar cobro otra vez, o Verificar estado.",
    billingButton: "Volver al inicio",
    billingNotesHeading: "Qué pasa ahora",
    billingNote1: "Stripe confirma los cambios del portal de facturación.",
    billingNote2:
      "El backend de HabloTruck recibe nuevas actualizaciones de suscripción o factura por webhook.",
    billingNote3: "ManyChat recibe el estado actualizado de recuperación de cobro.",
    notFoundTitle: "HabloTruck | Página No Encontrada",
    notFoundChip: "Página no encontrada",
    notFoundHeading: "No pudimos encontrar esa página.",
    notFoundLead:
      "Usa la página principal de estado o vuelve al chat de HabloTruck para continuar.",
    notFoundButton: "Ir al inicio",
  },
};

function resolveLanguage() {
  const url = new URL(window.location.href);
  const queryLang = url.searchParams.get("lang");
  if (queryLang === "en" || queryLang === "es") {
    localStorage.setItem("hablotruck-language", queryLang);
    return queryLang;
  }

  const saved = localStorage.getItem("hablotruck-language");
  if (saved === "en" || saved === "es") {
    return saved;
  }

  return navigator.language && navigator.language.toLowerCase().startsWith("es")
    ? "es"
    : "en";
}

function translateDocument(language) {
  const dict = translations[language] || translations.en;
  document.documentElement.lang = language;

  document.querySelectorAll("[data-i18n]").forEach((node) => {
    const key = node.dataset.i18n;
    if (dict[key]) {
      node.textContent = dict[key];
    }
  });

  document.querySelectorAll("[data-i18n-html]").forEach((node) => {
    const key = node.dataset.i18nHtml;
    if (dict[key]) {
      node.innerHTML = dict[key];
    }
  });

  document.querySelectorAll("[data-i18n-title]").forEach((node) => {
    const key = node.dataset.i18nTitle;
    if (dict[key]) {
      node.title = dict[key];
    }
  });

  if (dict[document.body.dataset.titleKey]) {
    document.title = dict[document.body.dataset.titleKey];
  }

  document.querySelectorAll("[data-lang-option]").forEach((button) => {
    const isActive = button.dataset.langOption === language;
    button.classList.toggle("active", isActive);
    button.setAttribute("aria-pressed", isActive ? "true" : "false");
  });

  document.querySelectorAll("a[href]").forEach((link) => {
    const href = link.getAttribute("href");
    if (!href || href.startsWith("http") || href.startsWith("#")) {
      return;
    }

    const url = new URL(href, window.location.href);
    url.searchParams.set("lang", language);
    link.setAttribute("href", `${url.pathname.split("/").pop()}${url.search}`);
  });
}

function setLanguage(language) {
  localStorage.setItem("hablotruck-language", language);
  const url = new URL(window.location.href);
  url.searchParams.set("lang", language);
  window.history.replaceState({}, "", url);
  translateDocument(language);
}

document.addEventListener("DOMContentLoaded", () => {
  const initialLanguage = resolveLanguage();
  translateDocument(initialLanguage);

  document.querySelectorAll("[data-lang-option]").forEach((button) => {
    button.addEventListener("click", () => setLanguage(button.dataset.langOption));
  });
});
