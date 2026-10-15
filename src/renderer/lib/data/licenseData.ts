import licenseMarkdown from '../../../../THIRD-PARTY-LICENSES.md?raw';
import projectLicense from '../../../../LICENSE?raw';
import { version } from '../../../../package.json';
import { parseLicenseMarkdown, type LicenseInfo } from '../../../shared/licenses';

// Loaded on demand by About and the license dialog; the full notice text stays
// out of the startup bundle and remains available offline.
export const OPEN_SOURCE_LICENSES: LicenseInfo[] = [
  { name: 'Auto MIDI Player', version, licenseName: 'GPL-3.0-or-later',
    url: 'https://github.com/Jed556/AutoMidiPlayer', licenseText: projectLicense },
  ...parseLicenseMarkdown(licenseMarkdown),
];
