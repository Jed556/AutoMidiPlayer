import test from 'node:test';
import assert from 'node:assert/strict';
import { contributorsFromCommits, createContributorLoader } from '../src/renderer/lib/data/contributors.ts';

const user = (login, id = 1, type = 'User') => ({
  login, id, type, html_url: `https://github.com/${login}`,
  avatar_url: `https://avatars.githubusercontent.com/u/${id}`
});
const commit = (author, { name = author?.login, email = `${name}@example.test`, message = 'Change', ...rest } = {}) => ({
  author, committer: user('Maintainer', 999), html_url: 'https://github.com/example/repo/commit/abc',
  commit: { author: { name, email }, message }, ...rest
});
const response = data => new Response(JSON.stringify(data));
const storage = () => {
  const data = new Map();
  return { data, getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) };
};

test('GitHub author associations credit alternate display names and emails, rather than the committer', () => {
  const result = contributorsFromCommits([
    commit(user('ActualAuthor'), { name: 'Different Git Name', email: 'one@example.test' }),
    commit(user('ACTUALAUTHOR'), { name: 'Another Alias', email: 'two@example.test' })
  ]);
  assert.equal(result.length, 1);
  assert.equal(result[0].profileUrl, 'https://github.com/ACTUALAUTHOR');
  assert.ok(!result.some(person => person.name === 'Maintainer'));
});

test('co-authors resolve ordinary emails, renamed account IDs and co-author-only noreply addresses', () => {
  const result = contributorsFromCommits([
    commit(user('Maintainer', 999), { message: 'Change\r\n\r\nCo-authored-by: Alias <HELPER@example.test>\r\n' +
      'Co-authored-by: Old Name <7+OldLogin@users.noreply.github.com>\r\n' +
      'Co-authored-by: Tester <8+OnlyCoauthor@users.noreply.github.com>\r\n' +
      'Co-authored-by: automation[bot] <automation@users.noreply.github.com>\r\n' +
      'Co-authored-by: Copilot <copilot@github.com>' }),
    commit(user('Helper', 6), { email: 'helper@example.test' }),
    commit(user('RenamedLogin', 7)),
    commit(user('service', 123, 'Bot'))
  ]);
  assert.deepEqual(result.map(person => person.name), ['Maintainer', 'Helper', 'RenamedLogin', 'OnlyCoauthor']);
  assert.equal(result.at(-1).avatarUrl, 'https://avatars.githubusercontent.com/u/8');
});

test('unlinked authors and co-authors keep their recorded names with source commit links', () => {
  const input = commit(null, { name: 'Unlinked Author', email: 'private@example.test',
    message: 'Change\n\nCo-authored-by: Unlinked Helper <helper@example.test>' });
  const result = contributorsFromCommits([input]);
  assert.deepEqual(result.map(person => person.name), ['Unlinked Author', 'Unlinked Helper']);
  assert.ok(result.every(person => person.profileUrl === input.html_url && person.avatarUrl === ''));
  assert.doesNotMatch(JSON.stringify(result), /@example.test/);
});

test('both default and legacy histories are fully paginated, merged and deduplicated', async () => {
  const calls = [];
  const loader = createContributorLoader(async url => {
    const query = new URL(url).searchParams;
    calls.push(query);
    if (!query.has('sha')) return response([commit(user('Current'))]);
    if (query.get('page') === '1') return response(Array.from({ length: 100 }, () => commit(user('OldAuthor', 2))));
    return response([commit(user('LateAuthor', 3)), commit(user('Current'))]);
  }, storage());
  const first = loader.load();
  assert.equal(loader.load(), first, 'concurrent mounts share one scan');
  const result = await first;
  assert.deepEqual(result.map(person => person.name), ['Current', 'OldAuthor', 'LateAuthor']);
  assert.equal(calls.length, 3);
  assert.ok(calls.every(query => query.get('per_page') === '100'));
  assert.deepEqual(calls.filter(query => query.has('sha')).map(query => [query.get('sha'), query.get('page')]),
    [['legacy', '1'], ['legacy', '2']]);
});

test('successful discovery persists and serves a fresh cache without making requests', async () => {
  const cache = storage();
  const first = createContributorLoader(async () => response([commit(user('Discovered'))]), cache, () => 1000);
  const expected = await first.load();
  const second = createContributorLoader(async () => assert.fail('fresh cache should not fetch'), cache, () => 2000);
  assert.deepEqual(second.readCached(), expected);
  assert.deepEqual(await second.load(), expected);
});

test('partial and rate-limited refreshes retain cached legacy people without marking the cache fresh', async t => {
  t.mock.method(console, 'warn', () => {});
  const cache = storage();
  const old = createContributorLoader(async () => response([commit(user('CachedLegacy'))]), cache, () => 1000);
  await old.load();
  const saved = [...cache.data.entries()];
  const loader = createContributorLoader(async url => new URL(url).searchParams.has('sha')
    ? new Response('rate limit', { status: 403 }) : response([commit(user('NewAuthor', 2))]), cache, () => 4000000);
  assert.deepEqual((await loader.load()).map(person => person.name), ['CachedLegacy', 'NewAuthor']);
  assert.deepEqual([...cache.data.entries()], saved);
  const offline = createContributorLoader(async () => { throw new Error('offline'); }, cache, () => 4000000);
  assert.deepEqual((await offline.load()).map(person => person.name), ['CachedLegacy']);
});

test('first-run branch errors still show successful results and retry on the next visit', async t => {
  t.mock.method(console, 'warn', () => {});
  let calls = 0;
  const cache = storage();
  const loader = createContributorLoader(async url => {
    calls++;
    return new URL(url).searchParams.has('sha')
      ? new Response('missing branch', { status: 404 }) : response([commit(user('Available'))]);
  }, cache);
  assert.deepEqual((await loader.load()).map(person => person.name), ['Available']);
  await loader.load();
  assert.equal(calls, 4);
  assert.equal(cache.data.size, 0);
});

test('unavailable storage and malformed API data do not reject the loader promise', async t => {
  t.mock.method(console, 'warn', () => {});
  const unavailable = { getItem() { throw new Error('blocked'); }, setItem() { throw new Error('blocked'); } };
  const loader = createContributorLoader(async () => response([commit(user('Author'))]), unavailable);
  assert.equal((await loader.load())[0].name, 'Author');
  assert.equal((await loader.load())[0].name, 'Author', 'in-memory cache survives failed persistence');
  const malformed = createContributorLoader(async () => response({ message: 'bad data' }), storage());
  assert.deepEqual(await malformed.load(), []);
});
