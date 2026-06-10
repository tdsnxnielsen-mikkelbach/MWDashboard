# Required Permissions

The app registration needs the following **Application** permissions (not Delegated):

| Permission | Purpose | Notes |
|-----------|---------|-------|
| `Reports.Read.All` | MAU usage reports, workload activity, user detail | Core functionality + Activity + Segmentation pages |
| `Organization.Read.All` | Subscribed SKUs / license data, organization info | License page + Consent callback (verify tenant domain) |
| `ServiceMessage.Read.All` | M365 Message Center posts | License page — Message Center section |
| `AuditLog.Read.All` | Entra sign-in logs | Security page — requires Entra ID P1/P2 on target tenant |
| `User.Read.All` | User department attribute | Department Adoption page — maps users to departments |

## Granting Consent

After adding permissions in Azure Portal → App Registrations → API Permissions, you must **re-grant admin consent** for each tenant. Either:
- Send the tenant admin the consent URL generated on the Tenants page, or
- Click "Grant admin consent" in the Azure Portal if you're in the home tenant

When a tenant admin grants consent via the generated URL, they are redirected to an isolated Static Web App (not the dashboard) which automatically registers the tenant and triggers initial data collection.

### Redirect URI Registration

The consent URL redirects to the Azure Static Web App after consent is granted. You must register this URL in the app registration:

1. Go to **Azure Portal → App registrations → your app → Authentication → Web → Redirect URIs**
2. Add the Static Web App URL from `azd env get-values` → `CONSENT_STATIC_URI`
   - Example: `https://swa-consent-xxxxx.azurestaticapps.net`

This URL is stable after first deployment and does not change unless you recreate the infrastructure.

## Entra ID License Requirements

| Target Tenant License | What's Available |
|----------------------|-----------------|
| Entra ID Free | MAU reports, licenses, Message Center, workload activity, segmentation, departments — **no sign-in logs** |
| Entra ID P1 | All above + sign-in logs (success/failure counts) |
| Entra ID P2 | All above + full AuthenticationDetails (MFA method breakdown) |

## Copilot Data Requirements

| Requirement | Details |
|-------------|--------|
| Microsoft 365 Copilot licenses | Tenant must have Copilot licenses assigned to users |
| `Reports.Read.All` permission | Same permission used for MAU reports; no additional consent needed |
| Beta API availability | Uses Microsoft Graph Beta endpoint `getMicrosoft365CopilotUsageUserDetail` |

If a tenant doesn't have Entra ID P1/P2, sign-in log collection will gracefully fail and log a warning — all other data collection continues normally.

## Dashboard User Authentication

The dashboard uses Azure AD OpenID Connect for user authentication (same app registration as Graph API access).

| User Type | Access Level | TenantSelector |
|-----------|-------------|----------------|
| Home tenant user | All registered tenants | Full multi-tenant selector |
| Customer tenant user | Own tenant data only | Tenant name shown (no selector) |

### Redirect URI Registration

In addition to the consent redirect URI (Static Web App), register the dashboard sign-in URI:

- **Sign-in**: `https://<web-app-url>/signin-oidc`
- **Sign-out**: `https://<web-app-url>/signout-callback-oidc`

### Access Control Logic

1. Home tenant (`AzureAd:TenantId`) — always allowed, sees all data
2. Customer tenants — must be registered and active in the database (via consent flow); data scoped to own tenant only
3. Unregistered/deactivated tenants — sign-in rejected, redirected to `/access-denied` page with error message and sign-out link

## Consent Troubleshooting

1. **"Insufficient privileges"** — The admin who consented may not be a Global Admin. Only Global Admins can consent to Application permissions.
2. **"Tenant is not registered"** on dashboard login — The tenant hasn't completed the consent flow yet. Send the admin the consent URL from the Tenants page.
3. **Sign-in logs returning 403** — Target tenant doesn't have Entra ID P1/P2 license.
4. **Message Center returning empty** — Ensure `ServiceMessage.Read.All` is granted and the tenant has M365 services active.
5. **Department data returning empty** — Ensure `User.Read.All` is granted. Some tenants may not populate the `department` field on user accounts.
6. **Copilot data returning empty** — Tenant must have Microsoft 365 Copilot licenses assigned. Data appears after users have been active with Copilot.
7. **Stale consent** — If you add new permissions after initial consent, you must re-consent. Use the consent URL generator on the Tenants page.
