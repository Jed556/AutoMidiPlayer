interface ConfigurationStartupStatus { configurationReviewRequired: boolean | null }

/** A launch with pending review must not import App or wake the native runtime yet. */
export async function prepareConfigurationStartup<T extends ConfigurationStartupStatus>(options: {
  initialStatus?: T;
  readStatus: () => Promise<T>;
  onStatus: (status: T) => void;
  review: () => Promise<void>;
  getSettings: () => Promise<{ onboardingCompleted?: boolean }>;
  startRuntime: () => Promise<unknown>;
  wait?: () => Promise<void>;
}): Promise<T | undefined> {
  let status = options.initialStatus;
  for (;;) {
    status ??= await options.readStatus();
    options.onStatus(status);
    if (status.configurationReviewRequired !== null) break;
    await (options.wait?.() ?? new Promise<void>(resolve => setTimeout(resolve, 50)));
    status = undefined;
  }
  if (!status.configurationReviewRequired) return status;
  await options.review();
  // Fresh installs still defer the runtime until the normal onboarding Finish step.
  if ((await options.getSettings()).onboardingCompleted) await options.startRuntime();
  return undefined;
}
