/**
 * Normalize a Buyer Workspace request into the ACTIVE-REQUEST contract the shared quotation-wizard
 * controller expects.
 *
 * The classic host feeds the controller a queue "group" object whose request GUID is `requestId`, and
 * the controller reads `wizardActiveRequest.requestId` for upload, OCR extract, save, reconcile, temp
 * cleanup, processing flags and replace-document. The Workspace instead loads a RequestDetailsDto
 * (api.requests.get), which exposes that GUID as `id` and has NO `requestId` — so without this mapping
 * every path posted to `/api/v1/attachments/upload/undefined` (HTTP 400 "The value 'undefined' is not
 * valid."). We stamp `requestId` from the GUID the wizard was opened for, once, at the host boundary —
 * keeping the shared controller's contract identical to classic and touching no controller internals.
 *
 * Kept in its own dependency-free module (no api/react imports) so it is unit-testable under the
 * repo's node-only vitest environment.
 */
export function toWizardActiveRequest<T extends object>(request: T, requestId: string): T & { requestId: string } {
    return { ...request, requestId };
}
