import licenseMarkdown from '../../../../THIRD-PARTY-LICENSES.md?raw';
import projectLicense from '../../../../LICENSE?raw';
import { version, dependencies } from '../../../../package.json';
import { parseLicenseMarkdown, type LicenseInfo } from '../../../shared/licenses';

// Loaded on demand by About and the license dialog; the full notice text stays
// out of the startup bundle and remains available offline.
export const OPEN_SOURCE_LICENSES: LicenseInfo[] = [
  { name: 'Auto MIDI Player', version, licenseName: 'GPL-3.0-or-later',
    url: 'https://github.com/Jed556/AutoMidiPlayer', licenseText: projectLicense },
  ...parseLicenseMarkdown(licenseMarkdown),
];

// Keep the default list focused on direct runtime dependencies and credits.
// All native and transitive notices remain available in the full list.
export const PRIMARY_OPEN_SOURCE_LICENSES = OPEN_SOURCE_LICENSES.filter(entry =>
  !entry.kind || entry.kind === 'attribution' || entry.kind === 'resource'
  || (entry.kind === 'javascript' && Object.hasOwn(dependencies, entry.name))
);
