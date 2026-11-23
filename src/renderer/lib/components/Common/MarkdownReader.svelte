<script lang="ts">
  import { parseMarkdown } from '../../utils/markdownParser';
  import { api } from '../../backend/index.js';

  interface Props {
    markdown: string;
    class?: string;
  }

  let {
    markdown = '',
    class: className = '',
  }: Props = $props();

  let htmlContent = $derived(parseMarkdown(markdown));

  function interceptLinks(node: HTMLElement) {
    function onClick(e: MouseEvent) {
      const target = e.target as HTMLElement | null;
      const anchor = target?.closest('a');
      if (anchor && anchor.href) {
        e.preventDefault();
        void api.settings.openUrl(anchor.href);
      }
    }
    node.addEventListener('click', onClick);
    return {
      destroy() {
        node.removeEventListener('click', onClick);
      }
    };
  }
</script>

<div
  class="markdown-reader {className}"
  use:interceptLinks
>
  {@html htmlContent}
</div>

<style>
  .markdown-reader {
    font-size: 13px;
    line-height: 1.6;
    color: var(--text-primary);
    word-break: break-word;
    user-select: text;
  }

  .markdown-reader :global(h1) {
    font-size: 17px;
    font-weight: 700;
    color: var(--text-primary);
    margin: 0 0 12px 0;
    padding-bottom: 6px;
    border-bottom: 1px solid var(--control-fill);
    letter-spacing: -0.01em;
  }

  .markdown-reader :global(h2) {
    font-size: 14.5px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 14px 0 8px 0;
    letter-spacing: -0.01em;
  }

  .markdown-reader :global(h1 + h2) {
    margin-top: 8px;
  }

  .markdown-reader :global(h3) {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 10px 0 6px 0;
  }

  .markdown-reader :global(h4),
  .markdown-reader :global(h5),
  .markdown-reader :global(h6) {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--text-secondary);
    margin: 8px 0 4px 0;
  }

  .markdown-reader :global(p) {
    margin: 0 0 8px 0;
    color: var(--text-secondary);
  }

  .markdown-reader :global(ul),
  .markdown-reader :global(ol) {
    margin: 0 0 10px 0;
    padding-left: 20px;
    color: var(--text-secondary);
  }

  .markdown-reader :global(li) {
    margin-bottom: 4px;
    line-height: 1.55;
  }

  .markdown-reader :global(li.task-list-item) {
    list-style-type: none;
    margin-left: -18px;
    display: flex;
    align-items: baseline;
    gap: 6px;
  }

  .markdown-reader :global(li.task-list-item input[type="checkbox"]) {
    margin: 0;
    position: relative;
    top: 2px;
  }

  .markdown-reader :global(blockquote) {
    margin: 10px 0;
    padding: 10px 14px;
    background: var(--control-fill);
    border-left: 3px solid var(--accent);
    border-radius: var(--radius-sm);
    color: var(--text-primary);
    font-size: 12.5px;
  }

  .markdown-reader :global(blockquote p) {
    margin: 0;
    color: var(--text-primary);
  }

  .markdown-reader :global(blockquote p + p) {
    margin-top: 6px;
  }

  .markdown-reader :global(a) {
    color: var(--accent);
    text-decoration: none;
    font-weight: 500;
    transition: color var(--transition-fast);
  }

  .markdown-reader :global(a:hover) {
    text-decoration: underline;
  }

  .markdown-reader :global(strong) {
    color: var(--text-primary);
    font-weight: 600;
  }

  .markdown-reader :global(code) {
    font-family: 'Cascadia Code', Consolas, 'Segoe UI Mono', monospace;
    font-size: 12px;
    background: var(--control-fill);
    padding: 1px 5px;
    border-radius: 4px;
    color: var(--text-primary);
  }

  .markdown-reader :global(pre) {
    margin: 8px 0 12px 0;
    background: var(--control-fill);
    border-radius: var(--radius-sm);
    padding: 10px 12px;
    overflow-x: auto;
  }

  .markdown-reader :global(pre code) {
    background: transparent;
    padding: 0;
    font-size: 12px;
    line-height: 1.5;
    display: block;
  }

  .markdown-reader :global(hr) {
    border: none;
    border-top: 1px solid var(--control-fill);
    margin: 14px 0;
  }

  .markdown-reader :global(table) {
    width: 100%;
    border-collapse: collapse;
    margin: 10px 0;
    font-size: 12.5px;
  }

  .markdown-reader :global(th),
  .markdown-reader :global(td) {
    padding: 6px 10px;
    border: 1px solid var(--control-fill);
    text-align: left;
  }

  .markdown-reader :global(th) {
    background: var(--control-fill);
    font-weight: 600;
    color: var(--text-primary);
  }
</style>
