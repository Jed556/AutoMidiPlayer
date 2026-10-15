<script lang="ts">
  import { onMount } from 'svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { loadOpenSourceLicenses, type LicenseInfo } from '../../data/aboutData';
  import FluentIcon from '../Common/FluentIcon.svelte';
  import ContentDialog from './ContentDialog.svelte';
  import { t } from '../../i18n';

  let licenses = $state<LicenseInfo[]>([]);
  onMount(() => {
    void loadOpenSourceLicenses().then(items => { licenses = items; });
  });

  let currentLicense = $derived.by<LicenseInfo | null>(() => {
    const data = uiStore.dialogData;
    if (!data) return null;
    if (typeof data === 'object' && data.licenseText) {
      return data as LicenseInfo;
    }
    if (typeof data === 'string') {
      return licenses.find(l => l.name.toLowerCase() === data.toLowerCase()) || null;
    }
    return null;
  });

  let selectedLicense = $state<LicenseInfo | null>(null);

  $effect(() => {
    selectedLicense = currentLicense;
  });

  function selectItem(lic: LicenseInfo) {
    selectedLicense = lic;
  }

  function backToList() {
    selectedLicense = null;
  }
</script>

<ContentDialog
  size="large"
  title={selectedLicense ? selectedLicense.name : t('dialogs.licensesTitle')}
  subtitle={selectedLicense ? `${selectedLicense.licenseName}${selectedLicense.version ? ` • ${selectedLicense.version}` : ''}` : undefined}
  icon={!selectedLicense ? 'ShieldCheckmark' : undefined}
  showBackButton={!!(selectedLicense && !currentLicense)}
  onBack={backToList}
  showCloseButton={true}
  onClose={() => uiStore.closeDialog()}
>
  {#if selectedLicense}
    <div class="license-text-container">
      {#if selectedLicense.url}
        <div class="license-url-row">
          <a href={selectedLicense.url} target="_blank" rel="noreferrer" class="license-link">
            <span>{selectedLicense.url}</span>
            <FluentIcon name="Open" size={13} />
          </a>
        </div>
      {/if}
      <pre class="license-pre">{selectedLicense.licenseText}</pre>
    </div>
  {:else}
    <div class="licenses-list">
      {#each licenses as item (`${item.name}@${item.version}`)}
        <button class="license-item-btn" onclick={() => selectItem(item)}>
          <div class="license-row">
            <span class="lib-name">{item.name} {item.version}</span>
            <span class="lib-type">{item.licenseName}</span>
          </div>
          {#if item.url}
            <span class="lib-url">{item.url}</span>
          {/if}
        </button>
      {/each}
    </div>
  {/if}
</ContentDialog>

<style>
  .license-text-container {
    display: flex;
    flex-direction: column;
    gap: 12px;
  }

  .license-url-row {
    margin-bottom: 2px;
  }

  .license-link {
    font-size: 13px;
    color: var(--accent);
    text-decoration: none;
    display: inline-flex;
    align-items: center;
    gap: 6px;
    word-break: break-all;
  }

  .license-link:hover {
    text-decoration: underline;
  }

  .license-pre {
    margin: 0;
    font-family: Consolas, 'Cascadia Code', 'Segoe UI Mono', monospace;
    font-size: 13px;
    line-height: 1.6;
    color: var(--text-primary);
    white-space: pre-wrap;
    word-break: break-word;
    background: var(--control-fill);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-md);
    padding: 18px 22px;
    user-select: text;
    tab-size: 4;
  }

  .licenses-list {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    gap: 12px;
  }

  .license-item-btn {
    background: var(--control-fill);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-md);
    padding: 12px 16px;
    display: flex;
    flex-direction: column;
    gap: 6px;
    text-align: left;
    cursor: pointer;
    transition: all var(--transition-fast);
  }

  .license-item-btn:hover {
    background: var(--bg-hover);
    border-color: var(--accent);
  }

  .license-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    width: 100%;
  }

  .lib-name {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-primary);
  }

  .lib-type {
    font-size: 11.5px;
    padding: 2px 8px;
    border-radius: 4px;
    background: var(--accent-subtle);
    color: var(--accent);
    font-weight: 600;
    white-space: nowrap;
  }

  .lib-url {
    font-size: 12px;
    color: var(--text-tertiary);
    word-break: break-all;
  }
</style>
