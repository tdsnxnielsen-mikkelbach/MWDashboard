# Required Permissions

The app registration needs the following **Application** permissions (not Delegated):

| Permission | Purpose | Notes |
|-----------|---------|-------|
| `Reports.Read.All` | MAU usage reports, workload activity, user detail | Core functionality + Activity + Segmentation pages |
| `Organization.Read.All` | Subscribed SKUs / license data, organization info | License page + Consent callback (verify tenant domain) |
| `ServiceMessage.Read.All` | M365 Message Center posts | License page — Message Center section |
| `AuditLog.Read.All` | Entra sign-in logs; MFA/auth method registration (`/reports/authenticationMethods/userRegistrationDetails`); inactive-account `signInActivity` | Security page — sign-in logs and inactive accounts require Entra ID P1/P2 on target tenant (the `signInActivity` property is premium-gated); MFA registration works on all tiers |
| `User.Read.All` | User department attribute; licensed/enabled member accounts & `signInActivity` for inactive-account analysis | Department Adoption page (all tiers); Inactive Accounts on Security page (requires P1/P2 for `signInActivity`) |
| `SecurityEvents.Read.All` | Microsoft Secure Score + control profiles | Secure Score page — security posture score, trend, and remediation actions |
| `ServiceHealth.Read.All` | M365 service health overviews + active issues | Service Health page — per-service status and active incidents/advisories |
| `DeviceManagementManagedDevices.Read.All` | Intune-managed device compliance state + OS breakdown | Identity & Devices page — Device Compliance tab |
| `Policy.Read.All` | Conditional Access policy inventory + coverage gaps (legacy-auth block, MFA grant) | Identity & Devices page — Conditional Access tab; Conditional Access is a premium feature so policies exist only on **Entra ID P1/P2** tenants (Free-tier tenants report none) |
| `IdentityRiskyUser.Read.All` | Identity Protection risky users by risk level | Identity & Devices page — Risky Users tab; **requires Entra ID P2** on target tenant |
| `Group.Read.All` | Microsoft 365 / security / Teams-connected group inventory + ownerless-group detection | Usage & Governance page — Groups & Teams tab |

> `Guest / external users` (Identity & Devices page) reuses the already-granted `User.Read.All` permission — no extra consent needed.

## How permissions appear in the UI

Throughout the dashboard, Graph permissions are shown with their official admin-consent display name followed by the scope in parentheses — e.g. **Read all users' full profiles (`User.Read.All`)** — so non-technical admins understand what is being requested while the exact scope stays copy-paste friendly.

- The reusable `<PermissionTag Scope="User.Read.All" />` Blazor component ([src/MWDashboard.Web/Components/Shared/PermissionTag.razor](../src/MWDashboard.Web/Components/Shared/PermissionTag.razor)) renders this format anywhere a permission is referenced (page descriptions, "no data" alerts). Pass `ShowName="false"` for a compact code-only form with the display name in a tooltip.
- The scope → display-name map is centralized in `GraphPermissions` ([src/MWDashboard.Shared/Models/GraphPermissions.cs](../src/MWDashboard.Shared/Models/GraphPermissions.cs)) — the single source of truth. Add new permissions there and both the UI component and code-side formatting pick them up automatically. Unknown scopes fall back to the raw code.
- The Tenants page formats the stored `TenantInfo.MissingPermissions` (comma-separated scopes) the same way via `GraphPermissions.DescribeWithScope(...)`, so the re-consent alert lists friendly names too.

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
| Entra ID Free | MAU reports, licenses, Message Center, workload activity, segmentation, departments, MFA registration, Secure Score, Service Health, device compliance, guest users — **no sign-in logs, no inactive-account analysis, no Conditional Access policies, no risky-user analysis** (`signInActivity`, Conditional Access, and Identity Protection are premium-gated) |
| Entra ID P1 | All above + sign-in logs (success/failure counts) + inactive/stale account analysis + Conditional Access policies |
| Entra ID P2 | All above + full AuthenticationDetails (MFA method breakdown) + risky-user (Identity Protection) analysis |

> The dashboard detects each tenant's tier from its license SKUs (`TenantEntraTier.FromLicenses`) and **skips** premium-gated collection on tenants that lack the required tier — sign-in summary and inactive accounts on free-tier tenants, and risky-user analysis on anything below P2 — logged at Information rather than as a permission error. The Tenants page shows a per-tenant **Plan** chip (Free/P1/P2) so this limitation is visible at a glance. Because `IdentityRiskyUser.Read.All` is P2-gated, it is intentionally **not** included in the consent probe (a 403 on a non-P2 tenant is a licensing limit, not a consent gap, and would otherwise produce a false re-consent flag).

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
4. Home-tenant-only pages — Settings (`/settings`) and Tenants (`/tenants`) are hidden from customer-tenant users; direct URL access silently redirects them to the dashboard

## Consent Troubleshooting

1. **"Insufficient privileges"** — The admin who consented may not be a Global Admin. Only Global Admins can consent to Application permissions.
2. **"Tenant is not registered"** on dashboard login — The tenant hasn't completed the consent flow yet. Send the admin the consent URL from the Tenants page.
3. **Sign-in logs / inactive accounts returning 403** — Target tenant doesn't have Entra ID P1/P2 license. This is a licensing limit, **not** a consent problem — re-consenting will not help. The Tenants page Plan chip shows the tenant's tier.
4. **Message Center returning empty** — Ensure `ServiceMessage.Read.All` is granted and the tenant has M365 services active.
5. **Department data returning empty** — Ensure `User.Read.All` is granted. Some tenants may not populate the `department` field on user accounts.
6. **"Re-consent" warning on the Tenants page** — A permission added to the app registration after the tenant originally consented is missing in that tenant. The per-tenant consent probe (run on each collection, or on demand via the "Check permissions" button) lists exactly which permissions failed. Send the tenant admin the consent URL from the Tenants page to re-approve. Premium-license limitations (P1/P2) are excluded from this warning, so a flagged permission always indicates a genuine consent gap.
6. **Copilot data returning empty** — Tenant must have Microsoft 365 Copilot licenses assigned. Data appears after users have been active with Copilot.
7. **Stale consent** — If you add new permissions after initial consent, you must re-consent. Use the consent URL generator on the Tenants page.
