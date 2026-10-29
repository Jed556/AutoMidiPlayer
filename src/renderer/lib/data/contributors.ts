import type { ContributorInfo } from './aboutData';

const REPOSITORY_API = 'https://api.github.com/repos/Jed556/AutoMidiPlayer';
const CACHE_KEY = 'amp:contributors:v1';
const CACHE_TTL = 60 * 60 * 1000;
type CacheStorage = Pick<Storage, 'getItem' | 'setItem'>;

function record(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' ? value as Record<string, unknown> : {};
}

function isBot(name: string, type?: unknown): boolean {
  return type === 'Bot' || /\[bot\]|^copilot$/i.test(name);
}

function account(value: unknown): ContributorInfo | undefined {
  const user = record(value);
  if (typeof user.login !== 'string' || !user.login || isBot(user.login, user.type) ||
      typeof user.html_url !== 'string' || typeof user.avatar_url !== 'string') return;
  return { name: user.login, profileUrl: user.html_url, avatarUrl: user.avatar_url };
}

/** Use GitHub's author association, never the committer's account or display name. */
export function contributorsFromCommits(commits: unknown[]): ContributorInfo[] {
  const people = new Map<string, ContributorInfo>();
  const byEmail = new Map<string, ContributorInfo>();
  const byId = new Map<string, ContributorInfo>();
  const botEmails = new Set<string>();
  const add = (person: ContributorInfo) => people.set(person.name.toLowerCase(), person);

  // Index every author first so trailers can resolve ordinary emails and renamed logins.
  for (const value of commits) {
    const item = record(value);
    const author = record(record(item.commit).author);
    const email = typeof author.email === 'string' ? author.email.toLowerCase() : '';
    const user = record(item.author);
    const person = account(user);
    if (person) {
      add(person);
      if (email) byEmail.set(email, person);
      if (typeof user.id === 'number') byId.set(String(user.id), person);
    } else if (isBot(String(user.login ?? author.name ?? ''), user.type)) {
      botEmails.add(email);
    }
  }

  function addIdentity(name: string, email: string, commitUrl: string) {
    const noreply = /^(?:(\d+)\+)?([a-z\d-]+)@users\.noreply\.github\.com$/i.exec(email);
    email = email.toLowerCase();
    if (!name.trim() || isBot(name) || botEmails.has(email)) return;
    const known = byEmail.get(email) || (noreply?.[1] ? byId.get(noreply[1]) : undefined) ||
      (noreply ? people.get(noreply[2].toLowerCase()) : undefined);
    if (known) {
      add(known);
    } else if (noreply) {
      add({
        name: noreply[2],
        profileUrl: `https://github.com/${noreply[2]}`,
        avatarUrl: noreply[1]
          ? `https://avatars.githubusercontent.com/u/${noreply[1]}`
          : `https://github.com/${noreply[2]}.png`
      });
    } else {
      // Unlinked emails have no reliable GitHub username. Credit the recorded
      // name and link the source commit instead of guessing an account.
      if (!people.has(name.toLowerCase())) add({ name, profileUrl: commitUrl, avatarUrl: '' });
    }
  }

  for (const value of commits) {
    const item = record(value);
    const commit = record(item.commit);
    const author = record(commit.author);
    const url = typeof item.html_url === 'string' ? item.html_url : '';
    if (!url.startsWith('https://github.com/')) continue;
    if (!item.author && typeof author.name === 'string' && typeof author.email === 'string') {
      addIdentity(author.name, author.email, url);
    }
    if (typeof commit.message !== 'string') continue;
    for (const match of commit.message.matchAll(/^Co-authored-by:\s*([^\r\n<>]+?)\s*<([^<>\s]+)>\s*$/gim)) {
      addIdentity(match[1].trim(), match[2], url);
    }
  }
  return [...people.values()];
}

function browserStorage(): CacheStorage | undefined {
  try { return globalThis.localStorage; } catch { return undefined; }
}

export function createContributorLoader(
  fetcher: typeof fetch = (...args) => fetch(...args),
  storage: CacheStorage | undefined = browserStorage(),
  now: () => number = Date.now
) {
  let cached: { updatedAt: number; people: ContributorInfo[] } | undefined;
  let pending: Promise<ContributorInfo[]> | undefined;
  try {
    const value = JSON.parse(storage?.getItem(CACHE_KEY) ?? 'null');
    if (value && typeof value.updatedAt === 'number' && Array.isArray(value.people) &&
        value.people.every((person: ContributorInfo) => person &&
          typeof person.name === 'string' && typeof person.profileUrl === 'string' &&
          person.profileUrl.startsWith('https://github.com/') && typeof person.avatarUrl === 'string')) {
      cached = value;
    }
  } catch { /* Storage may be unavailable or contain an incomplete write. */ }

  async function scan(ref?: string): Promise<unknown[]> {
    const commits: unknown[] = [];
    for (let page = 1; ; page++) {
      const query = new URLSearchParams({ per_page: '100', page: String(page) });
      if (ref) query.set('sha', ref);
      const response = await fetcher(`${REPOSITORY_API}/commits?${query}`, {
        headers: { Accept: 'application/vnd.github+json' },
        signal: AbortSignal.timeout(15000)
      });
      if (!response.ok) throw new Error(`GitHub contributors: HTTP ${response.status}`);
      const data: unknown = await response.json();
      if (!Array.isArray(data)) throw new Error('Invalid GitHub commit history');
      commits.push(...data);
      if (data.length < 100) return commits;
    }
  }

  async function refresh(): Promise<ContributorInfo[]> {
    const results = await Promise.allSettled([scan(), scan('legacy')]);
    const commits = results.flatMap(result => result.status === 'fulfilled' ? result.value : []);
    const people = contributorsFromCommits(commits);
    if (results.every(result => result.status === 'fulfilled')) {
      cached = { updatedAt: now(), people };
      try { storage?.setItem(CACHE_KEY, JSON.stringify(cached)); } catch { /* Keep the in-memory cache. */ }
      return people;
    }
    // Keep a complete earlier result through offline, deleted-branch or rate-limit errors.
    console.warn('[Contributors] Could not refresh both histories',
      results.filter(result => result.status === 'rejected').map(result => result.reason));
    const merged = new Map(cached?.people.map(person => [person.name.toLowerCase(), person]));
    for (const person of people) merged.set(person.name.toLowerCase(), person);
    return [...merged.values()];
  }

  return {
    readCached: () => cached?.people ?? [],
    load(): Promise<ContributorInfo[]> {
      if (cached && now() - cached.updatedAt < CACHE_TTL) return Promise.resolve(cached.people);
      if (!pending) pending = refresh().finally(() => { pending = undefined; });
      return pending;
    }
  };
}

export const contributorLoader = createContributorLoader();
