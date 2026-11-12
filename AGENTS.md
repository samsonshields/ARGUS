# ARGUS engineering rules

Read docs/project_specifications.md and docs/development-plan.md before changes.
Implement only the authorized phase. Web references Core contracts and calls the API;
only API/Infrastructure access SQLite. Keep business rules outside components and
controllers. Use migrations, async I/O, validated DTOs, UTC timestamps and GUID IDs.
Use isolated temporary databases in integration tests. Never commit local data or
secrets. Run build, tests and formatting checks; document unverified behavior.
