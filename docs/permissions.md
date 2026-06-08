# Required Permissions

The app registration needs the following **Application** permissions (not Delegated):

| Permission | Purpose | Notes |
|-----------|---------|-------|
| `Reports.Read.All` | MAU usage reports, workload activity, user detail | Core functionality + Activity + Segmentation pages |
| `Organization.Read.All` | Subscribed SKUs / license data | License page |
| `ServiceMessage.Read.All` | M365 Message Center posts | License page — Message Center section |
| `AuditLog.Read.All` | Entra sign-in logs | Security page — requires Entra ID P1/P2 on target tenant |
| `User.Read.All` | User department attribute | Department Adoption page — maps users to departments |

## Granting Consent

After adding permissions in Azure Portal → App Registrations → API Permissions, you must **re-grant admin consent** for each tenant. Either:
- Send the tenant admin the consent URL generated on the Tenants page, or
- Click "Grant admin consent" in the Azure Portal if you're in the home tenant

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

## Consent Troubleshooting

1. **"Insufficient privileges"** — The admin who consented may not be a Global Admin. Only Global Admins can consent to Application permissions.
2. **Sign-in logs returning 403** — Target tenant doesn't have Entra ID P1/P2 license.
3. **Message Center returning empty** — Ensure `ServiceMessage.Read.All` is granted and the tenant has M365 services active.
4. **Department data returning empty** — Ensure `User.Read.All` is granted. Some tenants may not populate the `department` field on user accounts.
5. **Copilot data returning empty** — Tenant must have Microsoft 365 Copilot licenses assigned. Data appears after users have been active with Copilot.
6. **Stale consent** — If you add new permissions after initial consent, you must re-consent. Use the consent URL generator on the Tenants page.
