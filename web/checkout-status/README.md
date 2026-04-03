# Checkout Status Site

This folder contains the static pages used as Stripe redirect targets for:

- individual checkout success
- individual checkout cancel
- company checkout success
- company checkout cancel
- billing portal return

The site is bilingual and lets the user switch between English and Spanish on every page.

## Pages

- `index.html`
- `success.html`
- `cancel.html`
- `company-success.html`
- `company-cancel.html`
- `billing-return.html`
- `404.html`

## Deployment Model

These files are deployed to the Azure Storage static website endpoint through the existing GitHub workflows.

The same infrastructure deployment now enables the static website feature on the environment storage account.

## Recommended Usage

Use the generated URLs from the deployment outputs:

- checkout success -> `.../success.html`
- checkout cancel -> `.../cancel.html`
- company success -> `.../company-success.html`
- company cancel -> `.../company-cancel.html`
- billing return -> `.../billing-return.html`

## Why This Lives In The Same Repo

This keeps:

- checkout redirect UX
- backend checkout contract
- infrastructure
- deployment automation

versioned together, while still keeping the frontend pages separate from the .NET Function App.
