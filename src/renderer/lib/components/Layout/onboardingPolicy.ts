/** Keep required setup and optional-step skipping policy in one place. */
export function canSkipOnboarding(step: number, pageCount: number, legacyCheckPending: boolean): boolean {
  return !legacyCheckPending && step >= 2 && step < pageCount - 1;
}
