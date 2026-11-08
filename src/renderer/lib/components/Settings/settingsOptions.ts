import type { AppSettings } from '$shared/types/settings';

/** Keep onboarding and Settings on the same localized update schedules. */
export function getUpdateCheckFrequencyOptions(t: (key: string) => string): {
  value: AppSettings['updateCheckFrequency'];
  label: string;
}[] {
  return [
    { value: '10min', label: t('settings.freq10Min') },
    { value: 'hourly', label: t('settings.freqHourly') },
    { value: 'daily', label: t('settings.freqDaily') },
    { value: 'weekly', label: t('settings.freqWeekly') },
    { value: 'launch', label: t('settings.freqLaunch') },
  ];
}
