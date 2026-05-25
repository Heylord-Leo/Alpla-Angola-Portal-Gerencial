# Security Incident Report: GitGuardian SMTP Secret Leak

## 1. Incident Overview

*   **Date/Time of Alert**: 2026-05-25T13:52:03+01:00 (Local Time)
*   **Repository**: `Heylord-Leo/Alpla-Angola-Portal-Gerencial`
*   **Secret Type**: SMTP Credentials (`smtp.azurecomm.net` / `donotreply@mail.alpla.com` password)
*   **Exposure Status**: **HISTORICAL LEAK** — Plaintext credentials were historically tracked in the repository within the `appsettings.Development.json` file. While the file has now been untracked and ignored in the current active HEAD, the historical commits still contain the plaintext passwords, triggering the GitGuardian alert.

---

## 2. Current Remediation Status (Active Head)

We have performed a complete audit of the active head of the `Portal-Gerencial_(Integração)` branch and confirm the following:

1.  **Untracked and Ignored**: `appsettings.Development.json` was successfully untracked from Git (`git rm --cached`) in commit `e1c1a91` and is no longer part of the active source files.
2.  **Gitignore Hardened**: `.gitignore` has been updated to include `appsettings.Development.json` and `secrets.json`, preventing any accidental future commits.
3.  **Active Codebase Clean**:
    *   **No active tracked file contains SMTP credentials** (server configuration uses placeholders, passwords are resolved dynamically from the database or user-secrets).
    *   **No active tracked file contains Primavera/Innux SQL passwords** (these were untracked along with `appsettings.Development.json`).
    *   **No active tracked file contains OpenAI API keys** (resolved dynamically at runtime via database and environment variables).

---

## 3. Required Credential Rotation Checklist

Because the Git history was public or shared, the exposed credentials must be treated as fully compromised. The following rotation procedures must be executed immediately:

*   [ ] **SMTP Password Rotation**: Coordinate with the network/system administrator to rotate the password for `donotreply@mail.alpla.com` on `smtp.azurecomm.net`.
*   [ ] **Database `sa` Credentials**: If the Primavera/Innux development passwords (`P@ssw0rd` and `ad#56&Hfe`) have been reused on Staging or Production databases, they must be changed immediately to strong, randomly generated secrets.
*   [ ] **OpenAI API Key**: (For caution) If any developer previously tested OpenAI API keys in a local configuration file that was tracked, revoke and rotate the API key through the OpenAI API platform.

---

## 4. Git History Cleanup Plan (Remediation)

Since Git is a content-addressable system, simply deleting or modifying a secret in a new commit does **NOT** remove it from the repository's database. The history must be rewritten to purge all references.

We recommend using **`git filter-repo`** (the modern, official successor to `git-filter-branch` and BFG Repo Cleaner) to completely scrub `appsettings.Development.json` from all historical commits.

### Cleanup Procedure

#### Step 4.1: Backup
Before rewriting history, create a local backup copy of the entire repository directory to guard against accidental data loss.
```bash
# In an independent folder, clone a backup copy
git clone --mirror https://github.com/Heylord-Leo/Alpla-Angola-Portal-Gerencial.git backup-repo.git
```

#### Step 4.2: Scrub File from History
Install `git-filter-repo` (requires Python) and execute the purge of `appsettings.Development.json`:
```bash
# Completely scrub appsettings.Development.json from all commits, branches, and tags
git filter-repo --path src/backend/AlplaPortal.Api/appsettings.Development.json --invert-paths
```

#### Step 4.3: Force Push to Remote
Coordinate a brief lock on the repository with all active collaborators, then force push the updated branches to remote:
```bash
# Push rewritten history to remote
git push origin --force --all
git push origin --force --tags
```

#### Step 4.4: Team Re-clone
All team members must discard their local copies and perform a fresh clone of the scrubbed repository to avoid re-introducing the leaked history:
```bash
# Discard old local clone and re-clone
git clone https://github.com/Heylord-Leo/Alpla-Angola-Portal-Gerencial.git
```

#### Step 4.5: Re-scan
Trigger a fresh manual scan on GitGuardian or GitHub Secret Scanning to confirm the leak has been resolved.
