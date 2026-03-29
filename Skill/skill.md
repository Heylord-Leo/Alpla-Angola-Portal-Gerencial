# Alpla Portal Feature Delivery

Use this skill whenever working on the Alpla Angola Portal Gerencial project.

## Working directory

Always use:
`C:\dev\alpla-portal`

## Delivery sequence

For each requested feature or fix:

1. understand the requested scope
2. review affected project docs before implementing
3. implement the change
4. run/build the affected app layers
5. if frontend/UX is affected, test in browser
6. reconcile all impacted documentation
7. report code changes, docs changes, tests, and remaining manual checks

## Standards

* follow `docs/MASTER_DATA_GUIDELINES.md`
* avoid hardcoding reusable business lookup values
* prefer master data + seed + lookup endpoints for reusable business values
* use inline validation for normal form validation errors
* use reusable masked currency inputs for money fields
* keep backend numeric persistence culture-safe
* preserve historical consistency for transactional data

## Documentation reconciliation

When implementation introduces or changes an official pattern:

* update the corresponding project documentation in the same task

Especially review:

* `FRONTEND_FOUNDATION.md`
* `DECISIONS.md`
* `ARCHITECTURE.md`
* `CHANGELOG.md`
* `VERSION.md`

## Browser QA

When frontend behavior changes, browser testing is mandatory.
Validate:

* actual page navigation
* inline validation behavior
* field highlights
* masked currency typing
* create/edit flows
* master data selection behavior

## Completion criteria

Do not consider the task complete if:

* code changed
* but docs were not reconciled
* or browser behavior was not verified for frontend changes
