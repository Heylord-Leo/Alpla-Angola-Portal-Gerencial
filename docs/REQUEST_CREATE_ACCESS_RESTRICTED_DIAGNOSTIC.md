# Diagnostic Checklist: "Acesso Restrito" on Novo Rascunho (RequestCreate)

> **Environment:** TEST on AOVIA1VMS011 — `https://portalgerencial-test.alpla.net/`
> **Version:** v2.155.1+
> **Date:** 2025-05-26

## Symptom

When a user navigates to **Compras & Logística → Pedidos → Novo Rascunho**, the page displays:

```
ACESSO RESTRITO
O seu utilizador não possui nenhuma Planta atribuída ao seu âmbito de acesso.
```

Even though the Admin User Management screen shows that the user has plant assignments (e.g., V1, V2, V3).

---

## Architecture Overview

The `RequestCreate` page loads user scope via **two separate mechanisms**:

| Source | Endpoint | When | Used For |
|--------|----------|------|----------|
| `AuthContext` (sessionStorage) | `POST /api/auth/login` | At login only | Sidebar, role checks |
| `api.users.me()` | `GET /api/v1/users/me` | On page load | Plant scope filtering |

**Critical:** The `RequestCreate` page calls `GET /api/v1/users/me` **live** on every page load. It does NOT rely on the stale `sessionStorage` user object.

---

## Step 1: Open Browser DevTools

1. Open **Microsoft Edge** or **Chrome**
2. Navigate to `https://portalgerencial-test.alpla.net/`
3. Log in as the affected user
4. Press **F12** to open DevTools
5. Click the **Console** tab
6. Click the **Network** tab (keep both visible if possible)

---

## Step 2: Reproduce the Issue

1. With DevTools open, navigate to: **Compras & Logística → Pedidos → Novo Rascunho**
2. Wait for the page to load completely
3. Check for the "ACESSO RESTRITO" message

---

## Step 3: Check Console for Errors

Look for the error message:

```
Falha ao carregar dados auxiliares
```

- **If present**: One of the 9 API calls on page load failed. The entire `Promise.all` was rejected.
- **If absent**: All API calls succeeded, but the `/me` response returned empty plants.

Also look for these diagnostic messages (added in v2.155.2+):

```
[RequestCreate] Profile loaded: X plant(s)
[RequestCreate] Auxiliary lookups loaded successfully
[RequestCreate] Plant scope filter: X allowed out of Y total
```

---

## Step 4: Inspect Network Requests

Filter the Network tab by `api/v1`. Look for the following requests:

### 4.1 — GET /api/v1/users/me (CRITICAL)

| Status | Meaning | Action |
|--------|---------|--------|
| **200** + `plants: ["V1","V2","V3"]` | ✅ Plants loaded correctly | Issue is in auxiliary lookups |
| **200** + `plants: []` | ❌ No plants in DB | Check `UserPlantScopes` table |
| **200** + `plants: ["","",""]` | ⚠️ Plant `Code` field is NULL | Fix plant master data |
| **401** | Token expired/invalid | User needs to log in again |
| **500** | Backend error | Check API logs on server |
| **No request** | Call never fired | Earlier call in the batch failed |

### 4.2 — GET /api/v1/lookups/need-levels?activeOnly=true

### 4.3 — GET /api/v1/lookups/departments?activeOnly=true

### 4.4 — GET /api/v1/lookups/companies?activeOnly=true

### 4.5 — GET /api/v1/lookups/plants?activeOnly=true

### 4.6 — GET /api/v1/lookups/request-types?activeOnly=true

### 4.7 — GET /api/v1/lookups/iva-rates?activeOnly=true

### 4.8 — GET /api/v1/lookups/units?activeOnly=true

### 4.9 — GET /api/v1/lookups/currencies?activeOnly=true

**If any of these returns a non-200 status**, and the code is pre-v2.155.2, it will cause the entire `Promise.all` to fail, hiding the plant scope result.

---

## Step 5: Inspect sessionStorage (Secondary Check)

In the Console tab, run:

```js
JSON.parse(sessionStorage.getItem('auth_user'))
```

Check the output for:
- `plants` — should be `["V1","V2","V3"]` (set at login time)
- `roles` — should include the user's assigned roles
- `departments` — should include department codes

> **Note:** This is the login-time snapshot. If plants were assigned **after** login, this will be stale. The user must log out and log in again to refresh this.

---

## Step 6: Verify Database (Read-Only SQL)

Run these queries on the **Portal-Gerencial-Test** database. These are read-only and safe.

### 6.1 — Find the user ID

```sql
SELECT Id, FullName, Email, IsActive
FROM Users
WHERE Email = 'leonardo.cintra@alpla.com';
```

### 6.2 — Check UserPlantScopes

```sql
SELECT ups.UserId, ups.PlantId, p.Code, p.Name, p.IsActive
FROM UserPlantScopes ups
INNER JOIN Plants p ON ups.PlantId = p.Id
WHERE ups.UserId = '<USER_ID_FROM_6.1>';
```

**Expected result:** 3 rows for V1, V2, V3 with `IsActive = 1`.

| Result | Meaning |
|--------|---------|
| 3 rows with V1, V2, V3 | ✅ DB is correct, issue is in API or frontend |
| 0 rows | ❌ Plants never saved to DB. Admin UI save may have failed |
| Rows with NULL or empty `Code` | ⚠️ Plant master data issue |

### 6.3 — Check UserDepartmentScopes

```sql
SELECT uds.UserId, uds.DepartmentId, d.Code, d.Name
FROM UserDepartmentScopes uds
INNER JOIN Departments d ON uds.DepartmentId = d.Id
WHERE uds.UserId = '<USER_ID_FROM_6.1>';
```

### 6.4 — Check Plant master data

```sql
SELECT Id, Code, Name, CompanyId, IsActive
FROM Plants
ORDER BY Code;
```

Verify that V1, V2, V3 exist, have non-null `Code`, and `IsActive = 1`.

---

## Decision Tree

```
START: User sees "ACESSO RESTRITO"
│
├─ Console shows "Falha ao carregar dados auxiliares"?
│  ├─ YES → One of the 9 API calls failed
│  │         Check Network tab for red/failed requests
│  │         Fix the failing endpoint (likely 500/401)
│  │         After v2.155.2: scope and lookups are independent
│  │
│  └─ NO → /me returned empty plants
│           Check /me response body
│           If plants is [] → check DB (query 6.2)
│           If plants is ["","",""] → fix Plant.Code in DB
│
├─ /me returns 401?
│  └─ Token expired. Log out and log in again.
│
├─ /me returns 500?
│  └─ Backend error. Check API logs:
│     C:\Apps\AlplaPortal\Test\api\logs\
│     Or enable stdoutLogEnabled in web.config
│
└─ DB query 6.2 returns 0 rows?
   └─ User-plant assignments are missing.
      Re-assign via Admin → Users → Edit → Plantas
      Verify the save succeeds (check Network tab for PUT /api/v1/users/{id})
```

---

## Post-Fix Verification

After resolving the issue:

1. **Log out** and **log in** again (refreshes sessionStorage and JWT)
2. Navigate to **Novo Rascunho**
3. Confirm the form loads with Company/Plant dropdowns populated
4. Check Console for diagnostic messages confirming plant count > 0
