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

export const INITIAL_CONTRIBUTORS: ContributorInfo[] = [
  {
    name: 'Jed556',
    avatarUrl: 'https://avatars.githubusercontent.com/u/84989546?v=4',
    profileUrl: 'https://github.com/Jed556'
  },
  {
    name: 'sabihoshi',
    avatarUrl: 'https://avatars.githubusercontent.com/u/25006819?v=4',
    profileUrl: 'https://github.com/sabihoshi'
  },
  {
    name: 'ChaiyoKung',
    avatarUrl: 'https://avatars.githubusercontent.com/u/45798076?v=4',
    profileUrl: 'https://github.com/ChaiyoKung'
  },
  {
    name: 'damarasf',
    avatarUrl: 'https://avatars.githubusercontent.com/u/36058272?v=4',
    profileUrl: 'https://github.com/damarasf'
  },
  {
    name: 'Kaishiro1008',
    avatarUrl: 'https://avatars.githubusercontent.com/u/142321923?v=4',
    profileUrl: 'https://github.com/Kaishiro1008'
  },
  {
    name: 'DavG25',
    avatarUrl: 'https://avatars.githubusercontent.com/u/31524206?v=4',
    profileUrl: 'https://github.com/DavG25'
  },
  {
    name: 'whateverzpy',
    avatarUrl: 'https://avatars.githubusercontent.com/u/86581757?v=4',
    profileUrl: 'https://github.com/whateverzpy'
  },
  {
    name: 'rinme',
    avatarUrl: 'https://avatars.githubusercontent.com/u/56862783?v=4',
    profileUrl: 'https://github.com/rinme'
  }
];

export async function loadOpenSourceLicenses() {
  return (await import('./licenseData')).OPEN_SOURCE_LICENSES;
}
