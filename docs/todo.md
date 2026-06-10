# TODO — Future Enhancements

## Partner Center API Integration (Cost/Billing Data)

**Priority**: High  
**Status**: Not started  
**Context**: The current consumption score is a proxy built from usage metrics (MAU, activity, storage, workload breadth). Microsoft 365 is per-user licensed, so there's no usage-based billing via Graph API. However, for CSP partners, the **Partner Center API** exposes actual billing/cost data per customer.

### What it enables
- True cost per tenant (monthly invoice amounts)
- License cost vs. consumption value comparison
- Cost-per-active-user metric
- ROI calculation per workload
- "Over-licensed" vs. "under-licensed" detection with real spend data

### API Details
- **Endpoint**: Partner Center REST API (`https://api.partnercenter.microsoft.com`)
- **Auth**: Separate app registration with Partner Center API permissions (different from Graph)
- **Key endpoints**:
  - `GET /v1/customers/{customer-id}/subscriptions` — subscriptions per customer
  - `GET /v1/invoices/{invoice-id}/lineitems` — billing line items
  - `GET /v1/customers/{customer-id}/subscriptions/usagerecords` — usage-based records
  - `GET /v1/customers/{customer-id}/orders` — order history

### Implementation Plan
1. Add Partner Center API authentication (separate `ClientSecretCredential` or Partner Center token flow)
2. Add `BillingSnapshot` model (TenantId, InvoiceMonth, TotalCharge, Currency, LicenseCount)
3. Map Partner Center `customer-id` to our `TenantId` (may need a mapping table or use tenant domain matching)
4. Add cost metrics to the Consumption page (cost/user, cost trend, cost vs. score correlation)
5. New page? `/cost` or extend `/consumption` with a "Cost" tab

### Prerequisites
- Partner Center account with admin agent or sales agent role
- App registration with Partner Center API access
- Mapping between Partner Center customers and registered tenants

---

## Other Future Items

- [x] Redis caching for consumption/storage queries (TTL 15 min) — `CachedMauDataService` decorator wraps `MauDataService`
- [x] M365 App Platform usage (`getM365AppUserCounts`) for cross-app engagement metrics — model, Graph endpoint, collection pipeline
- [x] Export consumption report to CSV — `/api/export/consumption` endpoint with download button on page
- [x] Separate on-demand collector container app (scales 0→3 independently, internal HTTP, with Web fallback)
- [x] Cache warm-up hosted service — pre-populates common queries on startup to avoid thundering herd
- [x] Sliding + absolute expiration — active dashboards stay warm (5/15 min or 20/60 min)
- [x] Cache multi-tenant combos — short 4-min TTL instead of bypassing cache entirely
- [x] Redis pub/sub cross-replica cache invalidation — `RedisCacheInvalidationService`
- [x] Automated consent flow with tenant auto-registration — Static Web App + Consent Callback container (HMAC-validated, Graph `/organization` verification, initial data collection trigger)
- [ ] Consumption score threshold alerts (email/Teams notification when score drops)
- [x] Historical comparison: month-over-month score change indicators — delta chips on all KPI cards
- [ ] Per-department consumption scoring (combine department usage + segmentation)
