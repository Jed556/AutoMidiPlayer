import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { parseMarkdown, parseInline, escapeHtml, sanitizeUrl } from '../src/renderer/lib/utils/markdownParser.ts';

test('HTML escaping prevents any injected markup', () => {
  assert.equal(escapeHtml('<script>alert("xss")</script>'), '&lt;script&gt;alert(&quot;xss&quot;)&lt;/script&gt;');
  assert.equal(escapeHtml('a & b > c < d "e" \'f\''), 'a &amp; b &gt; c &lt; d &quot;e&quot; &#39;f&#39;');
});

test('URL sanitization blocks dangerous protocols while preserving safe links', () => {
  assert.equal(sanitizeUrl('https://github.com/Jed556/AutoMidiPlayer'), 'https://github.com/Jed556/AutoMidiPlayer');
  assert.equal(sanitizeUrl('http://example.com'), 'http://example.com');
  assert.equal(sanitizeUrl('mailto:user@example.com'), 'mailto:user@example.com');
  assert.equal(sanitizeUrl('#section-updates'), '#section-updates');
  assert.equal(sanitizeUrl('javascript:alert(1)'), '#');
  assert.equal(sanitizeUrl('data:text/html,<script>alert(1)</script>'), '#');
  assert.equal(sanitizeUrl('vbscript:msgbox(1)'), '#');
});

test('Inline formatting correctly parses bold, italic, code, links, strikethrough', () => {
  assert.equal(parseInline('**bold**'), '<strong>bold</strong>');
  assert.equal(parseInline('*italic*'), '<em>italic</em>');
  assert.equal(parseInline('***bold italic***'), '<strong><em>bold italic</em></strong>');
  assert.equal(parseInline('`const x = 1;`'), '<code>const x = 1;</code>');
  assert.equal(parseInline('~~deleted~~'), '<del>deleted</del>');

  // Code spans preserve inner formatting syntax literally
  assert.equal(parseInline('`**not bold**`'), '<code>**not bold**</code>');

  // Links with formatted labels
  assert.equal(
    parseInline('[**Release**](https://github.com/Jed556)'),
    '<a href="https://github.com/Jed556" target="_blank" rel="noopener noreferrer"><strong>Release</strong></a>'
  );

  // Autolinks
  assert.equal(
    parseInline('See https://github.com/Jed556/AutoMidiPlayer for info'),
    'See <a href="https://github.com/Jed556/AutoMidiPlayer" target="_blank" rel="noopener noreferrer">https://github.com/Jed556/AutoMidiPlayer</a> for info'
  );
});

test('Headings and horizontal rules are parsed correctly', () => {
  const md = `# Heading 1\n## Heading 2\n### Heading 3\n---\n`;
  const html = parseMarkdown(md);
  assert.match(html, /<h1>Heading 1<\/h1>/);
  assert.match(html, /<h2>Heading 2<\/h2>/);
  assert.match(html, /<h3>Heading 3<\/h3>/);
  assert.match(html, /<hr \/>/);
});

test('HTML comments are stripped by default', () => {
  const md = `# Release v8.18.0\n\n<!-- release-prepared: v8.18.0 -->\n\n## What's New?`;
  const html = parseMarkdown(md);
  assert.doesNotMatch(html, /<!--/);
  assert.doesNotMatch(html, /release-prepared/);
  assert.match(html, /<h1>Release v8\.18\.0<\/h1>/);
  assert.match(html, /<h2>What&#39;s New\?<\/h2>/);
});

test('Blockquotes and nested callouts are parsed correctly', () => {
  const md = `> **macOS:** Gatekeeper notice [More details](https://github.com/Jed556)\n> Second line of note`;
  const html = parseMarkdown(md);
  assert.match(html, /<blockquote>/);
  assert.match(html, /<strong>macOS:<\/strong>/);
  assert.match(html, /<a href="https:\/\/github\.com\/Jed556"/);
  assert.match(html, /<\/blockquote>/);
});

test('Fenced code blocks are escaped and preserve newlines', () => {
  const md = "```typescript\nfunction test<T>(x: T): void {\n  console.log(x);\n}\n```";
  const html = parseMarkdown(md);
  assert.match(html, /<pre><code class="language-typescript">/);
  assert.match(html, /&lt;T&gt;/);
  assert.match(html, /console\.log\(x\);/);
});

test('Lists (ordered, unordered, and task lists) are parsed into proper tags', () => {
  const md = `- First item\n- Second **bold** item\n- Third item\n\n1. Number one\n2. Number two\n\n- [ ] Todo item\n- [x] Done item`;
  const html = parseMarkdown(md);
  assert.match(html, /<ul><li>First item<\/li>/);
  assert.match(html, /<li>Second <strong>bold<\/strong> item<\/li>/);
  assert.match(html, /<ol><li>Number one<\/li>/);
  assert.match(html, /<li class="task-list-item"><input type="checkbox" disabled \/> Todo item<\/li>/);
  assert.match(html, /<li class="task-list-item"><input type="checkbox" disabled checked \/> Done item<\/li>/);
});

test('Parses actual repo RELEASE.md accurately 1:1', () => {
  const releaseMd = readFileSync(new URL('../RELEASE.md', import.meta.url), 'utf8');
  const html = parseMarkdown(releaseMd);
  assert.match(html, /<h1>Release v8\.0\.0<\/h1>/);
  assert.match(html, /<p>Release notes are generated from the rewrite&#39;s commit history during release preparation\.<\/p>/);
  assert.match(html, /<strong>Full Changelog<\/strong>:/);
  assert.match(html, /<a href="https:\/\/github\.com\/Jed556\/AutoMidiPlayer\/commits\/v8\.0\.0"/);
});

test('Parses git-cliff / renderNotes output 1:1', () => {
  const generatedNotes = [
    '# Release v8.18.0',
    '',
    '<!-- release-prepared: v8.18.0 -->',
    '',
    "## What's New?",
    '',
    '- **Breaking:** **games:** New instrument ([#42](https://github.com/Jed556/AutoMidiPlayer/pull/42))',
    '- **audio:** High performance synthesis ([#43](https://github.com/Jed556/AutoMidiPlayer/pull/43))',
    '',
    '## Bug Fixes',
    '',
    '- Repair playback ([1234567](https://github.com/Jed556/AutoMidiPlayer/commit/1234567))',
    '',
    '## New Contributors',
    '',
    '- @new-user made their first contribution in [#42](https://github.com/Jed556/AutoMidiPlayer/pull/42).',
    '',
    "> **macOS:** The first launch is blocked by Gatekeeper because the app isn't notarized. Open it once, then choose **Open Anyway** in System Settings → Privacy & Security. [More details](https://github.com/Jed556/AutoMidiPlayer/wiki/Getting-Started#macos-first-launch)",
    '',
    '**Full Changelog**: [v8.17.0...v8.18.0](https://github.com/Jed556/AutoMidiPlayer/compare/v8.17.0...v8.18.0)',
  ].join('\n');

  const html = parseMarkdown(generatedNotes);

  // Verified 1:1 preservation of elements:
  assert.doesNotMatch(html, /<!--/);
  assert.match(html, /<h1>Release v8\.18\.0<\/h1>/);
  assert.match(html, /<h2>What&#39;s New\?<\/h2>/);
  assert.match(html, /<strong>Breaking:<\/strong> <strong>games:<\/strong> New instrument/);
  assert.match(html, /<a href="https:\/\/github\.com\/Jed556\/AutoMidiPlayer\/pull\/42"/);
  assert.match(html, /<h2>Bug Fixes<\/h2>/);
  assert.match(html, /<h2>New Contributors<\/h2>/);
  assert.match(html, /@new-user made their first contribution/);
  assert.match(html, /<blockquote>/);
  assert.match(html, /<strong>macOS:<\/strong>/);
  assert.match(html, /<strong>Full Changelog<\/strong>:/);
  assert.match(html, /<a href="https:\/\/github\.com\/Jed556\/AutoMidiPlayer\/compare\/v8\.17\.0\.\.\.v8\.18\.0"/);
});
