# Production-readiness TODO

This is the release checklist for turning Market List from a local/private prototype into an internet-facing service. Treat every **P0** item as a public-launch blocker. A checkbox is complete only when the change is implemented, tested, documented, and verified in a production-like environment.

## Definition of production ready

- [ ] Complete every P0 item below, with links to the test, runbook, dashboard, or deployment evidence.
- [ ] Document the supported deployment architecture, data flows, trust boundaries, and a lightweight threat model.
- [ ] Run a production-readiness review covering security, privacy, accessibility, operations, and rollback.
- [ ] Complete a database restore drill and an application rollback drill in staging.
- [ ] Record an owner and target release for every deferred P1/P2 item.

## P0 — Login and account management

### Registration and email ownership

- [ ] Integrate a transactional email provider using secrets supplied by the deployment environment.
- [ ] Require email confirmation before an account can sign in or join a household.
- [ ] Add confirmation-token expiry, resend confirmation, and change-unconfirmed-email flows.
- [ ] Return generic registration, login, resend, and recovery responses so an attacker cannot enumerate registered email addresses.
- [ ] Normalize and validate email addresses consistently through ASP.NET Core Identity; define how case, aliases, and changed addresses are handled.
- [ ] Add an acceptance test proving an unverified address cannot access authenticated or household endpoints.

### Passwords and recovery

- [ ] Add a forgot-password and reset-password flow using short-lived, single-use Identity tokens.
- [ ] Invalidate existing sessions after a password reset and offer “sign out everywhere” after a password change.
- [ ] Add an authenticated change-password flow that requires the current password and recent reauthentication.
- [ ] Replace the current eight-character minimum with a documented password policy that favors long passphrases, permits password managers, accepts a large maximum length, and rejects known-compromised passwords.
- [ ] Keep password-reset tokens, passwords, session cookies, and email-confirmation tokens out of URLs after use, application logs, analytics, and error reports.
- [ ] Test expired, reused, tampered, and cross-account reset and confirmation tokens.

### Brute-force and abuse protection

- [ ] Enable Identity lockout and change login to use `lockoutOnFailure: true`; choose and document thresholds and lockout duration.
- [ ] Apply partitioned rate limits to registration, login, recovery, confirmation resend, household join, and invite rotation endpoints.
- [ ] Return useful `429` responses without revealing whether an account exists, and emit metrics for throttles and lockouts.
- [ ] Add layered abuse controls at the reverse proxy or edge, including request/body limits and per-IP protections.
- [ ] Alert on abnormal login failures, reset requests, new-account creation, and invite-code guessing.

### Sessions and cookies

- [ ] Set the production session cookie to `Secure=Always`, `HttpOnly`, an explicit `SameSite` policy, `Path=/`, no `Domain` attribute, and an appropriate `__Host-` name such as `__Host-market-list.session`.
- [ ] Default “remember me” to off and define short idle, absolute, and remembered-session lifetimes; do not rely only on the current 14-day sliding expiration.
- [ ] Validate the Identity security stamp frequently enough to revoke compromised sessions, and revoke sessions after password, email, MFA, or account-security changes.
- [ ] Add a session-management screen that lists active sessions and supports revoking one or all other sessions.
- [ ] Persist Data Protection keys outside the container, encrypt/protect them at rest, control access, back them up, and document rotation and compromise response.
- [ ] Verify cookie and session behavior through the real HTTPS reverse proxy, including restarts, expiry, logout, multiple tabs, and clock skew.

### Account and household lifecycle

- [ ] Add profile management for display name and email; require reverification before a new email becomes trusted.
- [ ] Add account data export and account deletion with recent reauthentication and a safe confirmation flow.
- [ ] Define household roles (at minimum owner/member) and authorize invite rotation, member removal, household rename, and destructive actions accordingly.
- [ ] Add leave-household, remove-member, transfer-ownership, and last-owner/last-member behavior without orphaning or accidentally deleting data.
- [ ] Decide whether household invite codes are single-use or multi-use; add expiry, revocation, regeneration, audit events, and strong retry/rate limits.
- [ ] Stop returning an active invite code to members who are not authorized to manage invitations.
- [ ] Decide and document data retention after account deletion, household deletion, inactivity, and failed registration.

## P0 — Application and API security

### Request protection

- [ ] Add ASP.NET Core antiforgery services and middleware, issue a token to the React client, and require the token on every cookie-authenticated `POST`, `PUT`, `PATCH`, and `DELETE` request.
- [ ] Add integration tests proving missing, invalid, and cross-session antiforgery tokens are rejected while valid same-origin requests succeed.
- [ ] Keep the UI and API on one origin. If cross-origin clients are added later, use an explicit allowlist with carefully reviewed credentialed CORS rules—never a wildcard.
- [ ] Validate `Origin`/`Host` at the edge and configure ASP.NET Core forwarded headers to trust only known proxies.
- [ ] Apply global request-header, URL, JSON-body, and upload limits before adding any file-upload feature.

### Authorization and tenant isolation

- [ ] Centralize household membership/role authorization instead of relying on repeated endpoint queries.
- [ ] Add negative authorization tests for every resource endpoint: a user must never read, update, or delete another household's users, categories, groceries, or invitations, even with a valid foreign ID.
- [ ] Re-check authorization inside transactions for membership changes and destructive operations to reduce time-of-check/time-of-use races.
- [ ] Return consistent `401`, `403`, and `404` responses without exposing another household's existence.
- [ ] Review cascade-delete behavior—especially `Household -> ApplicationUser`—and require explicit service-layer workflows for destructive changes.

### Input, output, and browser hardening

- [ ] Move validation into shared request validators and enforce equivalent database constraints for required fields, lengths, normalized names, and valid enum/value ranges.
- [ ] Use consistent RFC 9457 Problem Details responses; suppress stack traces, SQL details, paths, and internal exception messages in production.
- [ ] Add a restrictive Content Security Policy and verify it against the built React assets; prefer self-hosted fonts and scripts.
- [ ] Send HSTS, `X-Content-Type-Options: nosniff`, a restrictive `Referrer-Policy`, clickjacking protection (`frame-ancestors`), and a minimal `Permissions-Policy`.
- [ ] Review caching: fingerprinted static assets may be immutable, but `index.html`, authenticated API responses, and account pages must not be cached as public content.
- [ ] Remove unnecessary response headers and disable production developer/debug endpoints and detailed errors.
- [ ] Run an OWASP ASVS-based review and record accepted risks rather than treating an automated scan as the security sign-off.

## P0 — Data and database safety

- [ ] Replace `Database.EnsureCreatedAsync()` and the hand-written startup DDL with committed EF Core migrations and `MigrateAsync()` only where operationally appropriate.
- [ ] Create and test a one-time migration path for databases already created by the prototype; do not mix `EnsureCreated` with migrations in production.
- [ ] Run migrations as a controlled, observable release job before application rollout, with a pre-migration backup and a documented forward-fix/rollback plan.
- [ ] Add transaction and concurrency handling for household moves, member removal, category rename/delete, invite rotation, and simultaneous grocery edits.
- [ ] Define SQLite operating settings and test them under expected load: WAL/journal mode, busy timeout, locking, disk-full behavior, corruption detection, and graceful shutdown.
- [ ] Keep production SQLite on durable local block storage and run one application replica only. Move to PostgreSQL before horizontal scaling, multi-region writes, or network-filesystem storage.
- [ ] Encrypt disks/volumes and backups; restrict database and Data Protection key access to the application identity and backup operators.
- [ ] Automate encrypted, off-host, versioned backups with retention and integrity checks; set explicit RPO/RTO targets and perform scheduled restore drills.
- [ ] Add capacity monitoring and alerts for disk space, database size, backup age/failure, restore failure, and SQLite lock contention.
- [ ] Classify stored personal data, minimize it, and document retention, export, deletion, and legal/privacy obligations.

## P0 — Deployment and platform hardening

- [ ] Upgrade the project, EF Core packages, SDK image, and runtime image from .NET 9 to the supported .NET 10 LTS line; verify migrations and behavior before deployment.
- [ ] Replace `latest` frontend dependency declarations with intentional versions, commit lockfiles, enable locked restores, and establish automated dependency updates.
- [ ] Pin Node and .NET container base images by digest in the release build and define a routine for rebuilding after base-image security updates.
- [ ] Terminate TLS with a trusted certificate, redirect HTTP to HTTPS, enable HSTS after validation, automate certificate renewal, and test renewal alerts.
- [ ] Configure trusted proxies/networks explicitly before honoring `X-Forwarded-*`; verify the application sees the correct scheme, host, and client IP.
- [ ] Make production startup fail on unsafe configuration, including insecure cookies, missing durable key storage, a development environment, or an ephemeral database path.
- [ ] Keep the non-root runtime user, drop unnecessary Linux capabilities, use a read-only root filesystem where possible, and mount only narrowly scoped writable data/temp paths.
- [ ] Set CPU, memory, PID, restart, and graceful-shutdown limits; verify the service recovers cleanly from termination and host restart.
- [ ] Split liveness and readiness checks. Readiness must verify dependencies needed to serve traffic without exposing sensitive diagnostic details.
- [ ] Generate an SBOM, scan source/dependencies/secrets/container images in CI, set a vulnerability-remediation policy, and sign/provenance-stamp release images.
- [ ] Publish immutable image tags, record the deployed version, retain a known-good image, and document a rollback that accounts for schema compatibility.
- [ ] Use a managed secret store for email credentials, monitoring keys, and future database credentials; document ownership, least privilege, and rotation.
- [ ] Maintain separate development, staging, and production configuration and credentials. Never restore unredacted production data into lower environments.

## P0 — Testing and delivery gates

- [ ] Add server unit tests for validation and business rules and integration tests backed by a real temporary database.
- [ ] Add client component tests for login, registration, recovery, error, loading, household, category, and grocery states.
- [ ] Add browser-level tests for register/confirm/login/logout/recover, household sharing, cross-household denial, category changes, and grocery collaboration.
- [ ] Test security-critical negative cases: CSRF, IDOR, brute-force/lockout, enumeration, stale sessions, revoked invites, malformed JSON, oversized requests, and privilege changes.
- [ ] Test migrations from every supported deployed schema and validate backup/restore against the newly built application.
- [ ] Add concurrency and failure-injection tests for simultaneous edits, database locks, disk full, email-provider failure, and interrupted deployment.
- [ ] Require CI to run formatting/linting, TypeScript build, .NET build with warnings reviewed, unit/integration/browser tests, migration checks, and security scans.
- [ ] Deploy every release to a production-like staging environment, run smoke tests through HTTPS, then use a controlled production rollout with automated health verification.
- [ ] Block releases when required checks fail; protect the release branch and require review for authentication, authorization, migrations, deployment, and dependency changes.

## P0 — Observability and operations

- [ ] Add structured logs with request/correlation IDs and deployment version; centralize them with access controls and retention.
- [ ] Redact passwords, cookies, tokens, email-confirmation/reset links, invite codes, authorization headers, and unnecessary personal data from all telemetry.
- [ ] Add tamper-resistant audit events for login/security changes, account deletion, household membership/role changes, invite actions, and administrative operations.
- [ ] Export metrics for traffic, latency, errors, saturation, process/container health, database failures/locks, email delivery, login failures, lockouts, throttles, and backup status.
- [ ] Define service-level objectives and alerts that are actionable, deduplicated, tested, and routed to a named responder.
- [ ] Create runbooks for outage triage, bad deploy rollback, database corruption/restore, full disk, certificate failure, email outage, account takeover, key/secret compromise, and data breach response.
- [ ] Define incident severity, escalation contacts, evidence retention, customer communication, and post-incident review procedures.
- [ ] Add uptime checks from outside the hosting platform and regularly test alerts, restore procedures, and on-call access.

## P0 — User trust, privacy, and accessibility

- [ ] Publish accurate privacy and terms/contact information before collecting real user data; document subprocessors such as hosting and email providers.
- [ ] Provide in-product access to data export, correction, deletion, and security contact/reporting paths.
- [ ] Do not add analytics or nonessential cookies until consent, retention, and privacy requirements are understood and documented.
- [ ] Meet WCAG 2.2 AA for authentication and core list workflows, including keyboard access, visible focus, labels, error announcements, contrast, reduced motion, and zoom/reflow.
- [ ] Test supported mobile and desktop browsers, slow/offline behavior, expired sessions, and accessible recovery flows.

## P1 — Important follow-up hardening

- [ ] Add phishing-resistant passkeys/WebAuthn and/or TOTP MFA with recovery codes and secure recovery procedures.
- [ ] Add notifications for password/email changes, new sessions, MFA changes, account deletion, and household membership changes.
- [ ] Add a user-visible recent-security-activity page with enough context to identify suspicious access without retaining excessive personal data.
- [ ] Introduce expiring, scoped invitation records instead of a single reusable code stored directly on `Household`; consider hashing invite secrets at rest.
- [ ] Add optimistic concurrency/version fields and clear conflict UI for collaborative edits.
- [ ] Run dependency, container, and dynamic application scans on a schedule as well as during release builds.
- [ ] Commission an independent penetration test before promoting the service beyond a small trusted audience, then track remediation and retest.
- [ ] Establish a vulnerability disclosure process and a maintained security contact such as `security.txt`.
- [ ] Add database maintenance procedures, trend capacity, and rehearse migration to PostgreSQL before SQLite becomes a constraint.

## P2 — Scale and resilience

- [ ] Move to a managed PostgreSQL service before adding multiple application replicas.
- [ ] Add distributed rate limiting, session/revocation state, and coordinated Data Protection key storage before horizontal scaling.
- [ ] Add multi-zone deployment and tested disaster recovery only after the database architecture supports it.
- [ ] Establish load/performance budgets, representative load tests, and autoscaling rules based on measured bottlenecks.
- [ ] Consider a durable job queue for email and other retryable side effects, with idempotency and dead-letter handling.

## Security references

- [ASP.NET Core antiforgery guidance](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery)
- [ASP.NET Core Identity account confirmation and password recovery](https://learn.microsoft.com/aspnet/core/security/authentication/accconfirm)
- [ASP.NET Core rate limiting middleware](https://learn.microsoft.com/aspnet/core/performance/rate-limit)
- [ASP.NET Core Data Protection](https://learn.microsoft.com/aspnet/core/security/data-protection/)
- [ASP.NET Core proxy and forwarded-header guidance](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer)
- [.NET support policy](https://dotnet.microsoft.com/platform/support/policy)
- [OWASP Application Security Verification Standard](https://owasp.org/www-project-application-security-verification-standard/)
- [OWASP Cheat Sheet Series](https://cheatsheetseries.owasp.org/)

## Notes about the current implementation

The current code already provides ASP.NET Core Identity password hashing, an HTTP-only same-site session cookie, household-scoped queries, cryptographically generated invite codes, persisted Data Protection keys, a non-root container user, and loopback-only Compose port binding. These are useful foundations, but they do not by themselves satisfy the launch gates above.