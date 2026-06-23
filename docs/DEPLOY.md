# Deployment Guide — Orkabi (Render + Neon + Google OAuth)

> **Manual steps — requires accounts.** This guide assumes you have (or will create):
> - A [Neon](https://neon.tech) account for the managed Postgres database
> - A [Render](https://render.com) account connected to your GitHub repository
> - A [Google Cloud](https://console.cloud.google.com) project with an OAuth 2.0 Client ID

---

## Step 1: Create the Neon Database

1. Log in to [neon.tech](https://neon.tech) and create a new project named `orkabi`.
2. From the project dashboard, navigate to **Connection Details** and copy **two separate connection strings**:

   - **Pooled endpoint** (the string contains `-pooler.<region>`): used for all runtime queries.
   - **Direct endpoint** (no `-pooler` in the hostname): used only during the boot migration.

3. Append the required parameters to each string:

   **`ConnectionStrings__Default`** (pooled, for runtime):
   ```
   Host=<your-pooler-host>;Database=neondb;Username=<user>;Password=<password>;SSL Mode=Require;Pooling=true;Maximum Pool Size=20;Max Auto Prepare=0
   ```

   **`ConnectionStrings__Migrations`** (direct, for boot migration only):
   ```
   Host=<your-direct-host>;Database=neondb;Username=<user>;Password=<password>;SSL Mode=Require
   ```

   > **Important:** `Max Auto Prepare=0` on the pooled string prevents the `prepared statement "_pN" already exists` / `does not exist` errors that occur when PgBouncer resets prepared-statement state between connections. If these errors appear in the Render boot logs, verify this parameter is present on `ConnectionStrings__Default`. The direct string (`Migrations`) must point to the non-pooler hostname so that the boot migration does not run through PgBouncer.

---

## Step 2: Configure Google OAuth

1. In [Google Cloud Console](https://console.cloud.google.com), open **APIs & Services → Credentials**.
2. Create or select an **OAuth 2.0 Client ID** of type "Web application".
3. Add the following **Authorized Redirect URIs**:
   - `https://<your-app-name>.onrender.com/signin-google` (production)
   - `https://localhost:<port>/signin-google` (local development, optional)
4. Copy the **Client ID** and **Client Secret**.

---

## Step 3: Deploy on Render

1. Push this repository to GitHub (if not already done).
2. Log in to [render.com](https://render.com) and create a new **Blueprint** by connecting your GitHub repository. Render will detect `render.yaml` automatically.
3. In the Render dashboard for the `orkabi` service, set the following **Environment Variables** (all marked `sync: false` in `render.yaml` so they are never committed):

   | Key | Value |
   |-----|-------|
   | `ConnectionStrings__Default` | Neon pooled string (with `Max Auto Prepare=0`) |
   | `ConnectionStrings__Migrations` | Neon direct string (without pooler) |
   | `Authentication__Google__ClientId` | Google OAuth Client ID |
   | `Authentication__Google__ClientSecret` | Google OAuth Client Secret |
   | `SEED_ADMIN_EMAIL` | Email address for the first admin user |
   | `SEED_ADMIN_PASSWORD` | Password for the first admin user |

4. Trigger a deploy.

---

## Step 4: Mandatory Acceptance Gate — Postgres Fidelity Check

After the first deploy, open the **Render service logs** for the boot sequence and verify:

- The log shows EF Core migrations applied against Neon (e.g., `Applying migration '20260622234201_InitialCreate'`).
- **No** errors of the form `prepared statement "_p1" already exists` or `prepared statement "_p1" does not exist`.

If either error appears:
- Check that `ConnectionStrings__Default` contains `Max Auto Prepare=0`.
- Check that `ConnectionStrings__Migrations` points to the **direct** (non-pooler) hostname, not the pooler.

**Do not mark Slice 0 complete until this check passes.** The SQLite inner loop (used in CI tests) cannot exercise the Npgsql migration files, Neon-specific connection behavior, PgBouncer pooling, or `timestamptz`/`DateOnly` types. The Render boot log is the sole Postgres-fidelity gate.

---

## Step 5: Post-Deploy Verification

1. Visit `https://<your-app>.onrender.com/health` → expect `{"status":"ok"}`.
2. Visit `/Account/Login` → expect the RTL Hebrew login page with correct Hebrew letterforms.
3. Log in with the seeded admin credentials (`SEED_ADMIN_EMAIL` / `SEED_ADMIN_PASSWORD`) → expect redirect to `/Dashboard/Admin`.
4. Click "המשך עם Google" → completes Google sign-in → expect a dashboard or `/Account/AccessDenied` if that Google account has no role assigned yet.
5. Confirm in the Neon dashboard that a row exists in `AspNetUsers` for the seeded admin.

---

## Step 6: Remove the Admin Password Variable

After the first successful deploy and verification:

1. In the Render dashboard, **delete** the `SEED_ADMIN_PASSWORD` environment variable.
2. Redeploy. The seeder will detect that the user already exists and skip creation (create-if-absent, idempotent).

---

## Notes

- **Cold start:** The first request after a period of inactivity on the Render free tier will take approximately 30–60 seconds. This is expected — Render spins down free services after 15 minutes of inactivity, and Neon also resumes its compute on demand. This is not a failure.
- **Connection pooling:** The app uses Neon's PgBouncer pooler for all runtime queries (`ConnectionStrings__Default`). The boot migration uses the direct endpoint (`ConnectionStrings__Migrations`) to avoid prepared-statement conflicts with PgBouncer in transaction mode.
- **Google OAuth redirect URI:** The callback path is `/signin-google` (ASP.NET Core Identity default). Do **not** use the page path `/Account/ExternalLogin` as the redirect URI.
