/**
 * Lightweight, secure, GitHub-Flavored Markdown parser for in-app release notes & documentation.
 * Produces sanitized, semantic HTML matching GitHub's release notes output 1:1.
 */

export interface MarkdownParserOptions {
  stripComments?: boolean;
}

/**
 * Escapes raw HTML special characters to prevent any injected markup.
 */
export function escapeHtml(str: string): string {
  return str
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

/**
 * Sanitizes URLs to prevent javascript: or data: URL injection.
 */
export function sanitizeUrl(url: string): string {
  const trimmed = url.trim();
  if (/^(https?:\/\/|mailto:|#)/i.test(trimmed)) {
    return trimmed.replace(/"/g, '&quot;');
  }
  return '#';
}

/**
 * Iteratively strips HTML comments until none remain, preventing nested comment bypasses.
 */
export function stripHtmlComments(input: string): string {
  let result = input;
  while (true) {
    const start = result.indexOf('<!--');
    if (start === -1) break;

    const stdEnd = result.indexOf('-->', start + 4);
    const bangEnd = result.indexOf('--!>', start + 4);

    let end: number;
    let endLen: number;
    if (stdEnd !== -1 && (bangEnd === -1 || stdEnd < bangEnd)) {
      end = stdEnd;
      endLen = 3;
    } else if (bangEnd !== -1) {
      end = bangEnd;
      endLen = 4;
    } else {
      break;
    }

    result = result.slice(0, start) + result.slice(end + endLen);
  }
  return result;
}

function formatEmphasis(text: string): string {
  let s = text;

  // Triple asterisks / underscores: bold italic
  s = s.replace(/\*\*\*([^*]+)\*\*\*/g, '<strong><em>$1</em></strong>');
  s = s.replace(/___([^_]+)___/g, '<strong><em>$1</em></strong>');

  // Double asterisks / underscores: bold
  s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
  s = s.replace(/__([^_]+)__/g, '<strong>$1</strong>');

  // Single asterisk / underscore: italic
  s = s.replace(/(^|[^\w*])\*([^*]+)\*([^\w*]|$)/g, '$1<em>$2</em>$3');
  s = s.replace(/(^|[^\w_])_([^_]+)_([^\w_]|$)/g, '$1<em>$2</em>$3');

  // Strikethrough: ~~text~~
  s = s.replace(/~~([^~]+)~~/g, '<del>$1</del>');

  return s;
}

/**
 * Parses inline markdown: bold, italic, strikethrough, inline code, links, and autolinks.
 */
export function parseInline(text: string): string {
  if (!text) return '';

  const placeholders: string[] = [];
  const makePlaceholder = (html: string) => {
    const idx = placeholders.length;
    placeholders.push(html);
    return `\x1aTOK${idx}\x1a`;
  };

  // 1. Strip HTML comments within inline text
  let s = stripHtmlComments(text);

  // 2. Escape HTML entities
  s = escapeHtml(s);

  // 3. Extract code spans first so their inner text is preserved untouched
  s = s.replace(/`([^`]+)`/g, (_, code) => {
    return makePlaceholder(`<code>${code}</code>`);
  });

  // 4. Extract explicit markdown links: [label](url)
  s = s.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_, label, url) => {
    const safeUrl = sanitizeUrl(url);
    const formattedLabel = formatEmphasis(label).replace(/\x1aTOK(\d+)\x1a/g, (_, idx) => placeholders[Number(idx)] ?? '');
    return makePlaceholder(`<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${formattedLabel}</a>`);
  });

  // 5. Autolink bare URLs (http:// or https://)
  s = s.replace(/(^|[\s(])(https?:\/\/[^\s)<]+)/g, (_, prefix, url) => {
    const safeUrl = sanitizeUrl(url);
    return `${prefix}${makePlaceholder(`<a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${url}</a>`)}`;
  });

  // 6. Bold, italic, and strikethrough formatting
  s = formatEmphasis(s);

  // 7. Restore placeholders
  s = s.replace(/\x1aTOK(\d+)\x1a/g, (_, idx) => placeholders[Number(idx)] ?? '');

  return s;
}

interface ListItem {
  indent: number;
  ordered: boolean;
  checked: boolean | null;
  text: string;
}

/**
 * Parses markdown blocks: headings, lists, blockquotes, code blocks, tables, hr, and paragraphs.
 */
export function parseMarkdown(markdown: string, options: MarkdownParserOptions = { stripComments: true }): string {
  if (!markdown || !markdown.trim()) return '';

  let raw = markdown.replace(/\r\n/g, '\n').replace(/\r/g, '\n');

  if (options.stripComments !== false) {
    raw = stripHtmlComments(raw);
  }

  const lines = raw.split('\n');
  const out: string[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];
    const trimmed = line.trim();

    // Skip empty lines
    if (!trimmed) {
      i++;
      continue;
    }

    // 1. Fenced Code Block: ```lang ... ```
    if (trimmed.startsWith('```')) {
      const lang = trimmed.slice(3).trim();
      i++;
      const codeLines: string[] = [];
      while (i < lines.length && !lines[i].trim().startsWith('```')) {
        codeLines.push(lines[i]);
        i++;
      }
      if (i < lines.length) i++; // consume closing ```
      const escapedCode = escapeHtml(codeLines.join('\n'));
      const classAttr = lang ? ` class="language-${escapeHtml(lang)}"` : '';
      out.push(`<pre><code${classAttr}>${escapedCode}</code></pre>`);
      continue;
    }

    // 2. Headings: # Heading 1 to ###### Heading 6
    const headingMatch = line.match(/^(#{1,6})\s+(.+)$/);
    if (headingMatch) {
      const level = headingMatch[1].length;
      const content = parseInline(headingMatch[2].trim());
      out.push(`<h${level}>${content}</h${level}>`);
      i++;
      continue;
    }

    // 3. Horizontal Rule: ---, ***, ___
    if (/^(?:---|\*\*\*|___)\s*$/.test(trimmed)) {
      out.push('<hr />');
      i++;
      continue;
    }

    // 4. Blockquotes: > ...
    if (trimmed.startsWith('>')) {
      const quoteLines: string[] = [];
      while (i < lines.length && (lines[i].trim().startsWith('>') || (lines[i].trim() && !lines[i].trim().startsWith('#') && !lines[i].trim().startsWith('-') && !lines[i].trim().startsWith('*') && !lines[i].trim().startsWith('```')))) {
        if (lines[i].trim().startsWith('>')) {
          quoteLines.push(lines[i].trim().replace(/^>\s?/, ''));
        } else {
          // Blockquote continuation
          quoteLines.push(lines[i].trim());
        }
        i++;
      }
      const alertMatch = quoteLines[0]?.trim().match(/^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\]$/i);
      if (alertMatch) {
        const alertType = alertMatch[1].toLowerCase();
        const alertLabel = alertType.charAt(0).toUpperCase() + alertType.slice(1);
        const bodyHtml = parseMarkdown(quoteLines.slice(1).join('\n'), options);
        out.push(`<blockquote class="markdown-alert markdown-alert-${alertType}"><p class="markdown-alert-title">${alertLabel}</p>${bodyHtml}</blockquote>`);
      } else {
        const quoteHtml = parseMarkdown(quoteLines.join('\n'), options);
        out.push(`<blockquote>${quoteHtml}</blockquote>`);
      }
      continue;
    }

    // 5. Tables: | Header | Header |
    if (trimmed.startsWith('|') && trimmed.endsWith('|') && i + 1 < lines.length && /^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$/.test(lines[i + 1].trim())) {
      const headerCells = trimmed.split('|').slice(1, -1).map(c => parseInline(c.trim()));
      i += 2; // skip header and delimiter

      const bodyRows: string[][] = [];
      while (i < lines.length && lines[i].trim().startsWith('|') && lines[i].trim().endsWith('|')) {
        const rowCells = lines[i].trim().split('|').slice(1, -1).map(c => parseInline(c.trim()));
        bodyRows.push(rowCells);
        i++;
      }

      let tableHtml = '<table><thead><tr>';
      for (const th of headerCells) {
        tableHtml += `<th>${th}</th>`;
      }
      tableHtml += '</tr></thead><tbody>';
      for (const row of bodyRows) {
        tableHtml += '<tr>';
        for (const td of row) {
          tableHtml += `<td>${td}</td>`;
        }
        tableHtml += '</tr>';
      }
      tableHtml += '</tbody></table>';
      out.push(tableHtml);
      continue;
    }

    // 6. Lists (ordered, unordered, task lists)
    const listMatch = line.match(/^(\s*)([-*+]|\d+\.)\s+(.*)$/);
    if (listMatch) {
      const items: ListItem[] = [];

      while (i < lines.length) {
        const currLine = lines[i];
        const currMatch = currLine.match(/^(\s*)([-*+]|\d+\.)\s+(.*)$/);

        if (currMatch) {
          const indent = currMatch[1].replace(/\t/g, '    ').length;
          const marker = currMatch[2];
          const isOrdered = /^\d+\./.test(marker);
          let itemText = currMatch[3].trim();
          let checked: boolean | null = null;

          // Check for task list: [ ] or [x]
          const taskMatch = itemText.match(/^\[([ xX])\]\s+(.*)$/);
          if (taskMatch) {
            checked = taskMatch[1].toLowerCase() === 'x';
            itemText = taskMatch[2].trim();
          }

          items.push({
            indent,
            ordered: isOrdered,
            checked,
            text: itemText,
          });
          i++;
        } else if (currLine.trim() && (currLine.startsWith('  ') || currLine.startsWith('\t')) && items.length > 0) {
          // Continuation line of previous list item
          items[items.length - 1].text += ' ' + currLine.trim();
          i++;
        } else {
          break;
        }
      }

      out.push(renderListItems(items));
      continue;
    }

    // 7. Regular Paragraph
    const paraLines: string[] = [];
    while (i < lines.length && lines[i].trim()) {
      const current = lines[i].trim();
      // Stop if a new block element starts
      if (current.startsWith('#') || current.startsWith('```') || current.startsWith('>') || /^(?:---|\*\*\*|___)\s*$/.test(current) || current.match(/^(\s*)([-*+]|\d+\.)\s+/)) {
        break;
      }
      paraLines.push(current);
      i++;
    }

    if (paraLines.length > 0) {
      const paraText = paraLines.join(' ');
      out.push(`<p>${parseInline(paraText)}</p>`);
    }
  }

  return out.join('\n');
}

/**
 * Renders structured list items into HTML, handling nested sublists and task checkboxes.
 */
function renderListItems(items: ListItem[]): string {
  if (!items.length) return '';

  let html = '';
  const stack: { tag: 'ul' | 'ol'; indent: number }[] = [];

  for (let j = 0; j < items.length; j++) {
    const item = items[j];
    const targetTag = item.ordered ? 'ol' : 'ul';

    // If indent is greater than current top of stack, push a new sublist
    if (stack.length === 0 || item.indent > stack[stack.length - 1].indent) {
      stack.push({ tag: targetTag, indent: item.indent });
      html += `<${targetTag}>`;
    } else {
      // Pop closed sublists
      while (stack.length > 1 && item.indent < stack[stack.length - 1].indent) {
        const closed = stack.pop()!;
        html += `</${closed.tag}></li>`;
      }
      // If tag type changed at the same level
      if (stack.length > 0 && stack[stack.length - 1].tag !== targetTag) {
        const closed = stack.pop()!;
        html += `</${closed.tag}><${targetTag}>`;
        stack.push({ tag: targetTag, indent: item.indent });
      }
    }

    const taskClass = item.checked !== null ? ' class="task-list-item"' : '';
    const checkbox = item.checked !== null
      ? `<input type="checkbox" disabled ${item.checked ? 'checked ' : ''}/> `
      : '';

    html += `<li${taskClass}>${checkbox}${parseInline(item.text)}`;

    // If next item does not indent deeper, close this <li>
    const nextItem = items[j + 1];
    if (!nextItem || nextItem.indent <= item.indent) {
      html += '</li>';
    }
  }

  while (stack.length > 0) {
    const closed = stack.pop()!;
    html += `</${closed.tag}>`;
  }

  return html;
}
