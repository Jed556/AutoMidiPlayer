export type { LicenseInfo } from '../../../shared/licenses';

export interface AboutLink {
  name: string;
  icon: string;
  url: string;
}

export interface ContributorInfo {
  name: string;
  profileUrl: string;
  avatarUrl: string;
}

export const ABOUT_LINKS: AboutLink[] = [
  {
    name: 'Release notes',
    icon: 'Notepad24',
    url: 'https://github.com/Jed556/AutoMidiPlayer/releases'
  },
  {
    name: 'Report a bug',
    icon: 'Bug24',
    url: 'https://github.com/Jed556/AutoMidiPlayer/issues/new?template=bug_report.yml'
  },
  {
    name: 'Feature request',
    icon: 'Flash24',
    url: 'https://github.com/Jed556/AutoMidiPlayer/issues/new?template=feature_request.yml'
  },
  {
    name: 'Wiki',
    icon: 'Lightbulb28',
    url: 'https://github.com/Jed556/AutoMidiPlayer/wiki'
  }
];

export async function loadOpenSourceLicenses() {
  return (await import('./licenseData')).OPEN_SOURCE_LICENSES;
}
