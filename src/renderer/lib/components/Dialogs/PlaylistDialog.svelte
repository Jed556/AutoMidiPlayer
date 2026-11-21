<script lang="ts">
  import { onMount } from 'svelte';
  import { uiStore } from '../../state/uiStore.svelte';
  import { isBuiltInPlaylist, libraryStore, type PlaylistInfo } from '../../state/libraryStore.svelte';
  import { animationStore } from '../../state/animationStore.svelte';
  import { notificationCenter } from '../../ui/notificationCenter.svelte';
  import { PLAYLIST_ICONS, PLAYLIST_THEMES, findMatchingTheme } from '../../utils/playlistPresets';
  import { t } from '../../i18n';
  import Button from '../Common/Button.svelte';
  import FluentIcon from '../Icons/FluentIcon.svelte';
  import ContentDialog from './ContentDialog.svelte';

  interface PlaylistDialogData {
    mode: 'create' | 'edit';
    playlist?: PlaylistInfo;
    initialSongIds?: string[];
  }

  const data = (uiStore.dialogData as PlaylistDialogData | null) ?? { mode: 'create' };
  const isEdit = data.mode === 'edit' && !!data.playlist;
  const targetPlaylist = data.playlist;
  const initialTheme = findMatchingTheme(targetPlaylist?.gradient, targetPlaylist?.bannerColor);
  const initialIcon = targetPlaylist?.icon && PLAYLIST_ICONS.includes(targetPlaylist.icon as any)
    ? targetPlaylist.icon
    : PLAYLIST_ICONS[0];

  let title = $state(isEdit ? targetPlaylist!.title : '');
  let description = $state(isEdit ? (targetPlaylist?.description ?? '') : '');
  let selectedTheme = $state(initialTheme);
  let selectedIcon = $state<string>(initialIcon);
  let isSaving = $state(false);
  let error = $state('');
  let nameInput: HTMLInputElement | undefined = $state();

  onMount(() => {
    nameInput?.focus();
    nameInput?.select();
  });

  async function save() {
    if (isSaving) return;
    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
      error = t('playlists.nameRequired');
      nameInput?.focus();
      return;
    }
    if (isEdit && (!targetPlaylist || isBuiltInPlaylist(targetPlaylist.id))) {
      return;
    }

    const trimmedDescription = description.trim();
    isSaving = true;
    error = '';

    try {
      if (isEdit && targetPlaylist) {
        await libraryStore.updatePlaylist(targetPlaylist.id, {
          title: trimmedTitle,
          subtitle: trimmedDescription,
          icon: selectedIcon,
          gradient: selectedTheme.gradient,
          bannerColor: selectedTheme.bannerColor,
        });
        notificationCenter.success(
          t('playlists.updatedTitle'),
          t('playlists.updatedMsg', { title: trimmedTitle })
        );
      } else {
        const created = await libraryStore.createPlaylist(
          trimmedTitle,
          selectedIcon,
          selectedTheme.bannerColor,
          trimmedDescription || undefined,
          selectedTheme.gradient,
        );
        const createdId = created?.id as string | undefined;
        if (createdId && data.initialSongIds?.length) {
          await libraryStore.addSongsToPlaylist(createdId, data.initialSongIds);
        }
        if (createdId) {
          libraryStore.selectedPlaylistId = createdId;
        }
        uiStore.activeTab = 'songs';
        notificationCenter.success(
          t('playlists.createdTitle'),
          t('playlists.createdMsg', { title: trimmedTitle })
        );
      }
      uiStore.closeDialog();
    } catch (e) {
      error = String(e);
      isSaving = false;
    }
  }

  function handleFormKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void save();
    }
  }
</script>

<ContentDialog
  title={isEdit ? t('playlists.editTitle') : t('playlists.createTitle')}
  icon={isEdit ? 'Edit' : 'Add'}
  class="playlist-dialog"
  onClose={() => uiStore.closeDialog()}
>
  <div class="playlist-form" class:no-motion={animationStore.isOff}>
    <div class="preview-card">
      <div class="preview-art" style="background: {selectedTheme.gradient};">
        <FluentIcon name={selectedIcon} filled={true} size={32} />
      </div>
      <div class="preview-meta">
        <span class="preview-badge">{t('playlists.defaultDescription')}</span>
        <div class="preview-title">{title.trim() || t('playlists.namePlaceholder')}</div>
        {#if description.trim()}
          <div class="preview-desc">{description.trim()}</div>
        {/if}
      </div>
    </div>

    <div class="form-section">
      <div class="form-field">
        <label for="playlist-name-input">{t('playlists.nameLabel')}</label>
        <input
          id="playlist-name-input"
          bind:this={nameInput}
          type="text"
          maxlength="80"
          placeholder={t('playlists.namePlaceholder')}
          bind:value={title}
          onkeydown={handleFormKeydown}
        />
      </div>

      <div class="form-field">
        <label for="playlist-desc-input">{t('playlists.descriptionLabel')}</label>
        <input
          id="playlist-desc-input"
          type="text"
          maxlength="160"
          placeholder={t('playlists.descriptionPlaceholder')}
          bind:value={description}
          onkeydown={handleFormKeydown}
        />
      </div>
    </div>

    <div class="form-section">
      <div class="section-label">{t('playlists.themeLabel')}</div>
      <div class="theme-grid" role="radiogroup" aria-label={t('playlists.themeLabel')}>
        {#each PLAYLIST_THEMES as theme (theme.id)}
          {@const active = selectedTheme.id === theme.id}
          <button
            type="button"
            class="theme-swatch"
            class:active
            style="background: {theme.gradient};"
            title={theme.name}
            aria-label={theme.name}
            aria-checked={active}
            role="radio"
            onclick={() => { selectedTheme = theme; }}
          >
            {#if active}
              <FluentIcon name="Checkmark" size={14} />
            {/if}
          </button>
        {/each}
      </div>
    </div>

    <div class="form-section">
      <div class="section-label">{t('playlists.iconLabel')}</div>
      <div class="icon-grid" role="radiogroup" aria-label={t('playlists.iconLabel')}>
        {#each PLAYLIST_ICONS as iconName (iconName)}
          {@const active = selectedIcon === iconName}
          <button
            type="button"
            class="icon-choice"
            class:active
            title={iconName}
            aria-label={iconName}
            aria-checked={active}
            role="radio"
            onclick={() => { selectedIcon = iconName; }}
          >
            <FluentIcon name={iconName} size={18} filled={active} />
          </button>
        {/each}
      </div>
    </div>

    {#if error}
      <p class="form-error" role="alert">{error}</p>
    {/if}
  </div>

  {#snippet footer()}
    <div class="dialog-actions">
      <Button variant="secondary" disabled={isSaving} onclick={() => uiStore.closeDialog()}>
        {t('dialogs.cancel')}
      </Button>
      <Button variant="accent" disabled={isSaving} onclick={() => void save()}>
        {isSaving
          ? t('playlists.savingAction')
          : (isEdit ? t('playlists.saveAction') : t('playlists.createAction'))}
      </Button>
    </div>
  {/snippet}
</ContentDialog>

<style>
  .playlist-form {
    display: flex;
    flex-direction: column;
    gap: 14px;
    padding-top: 2px;
  }

  .preview-card {
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 12px;
    border-radius: var(--radius-md, 8px);
    background: var(--control-fill);
  }

  .preview-art {
    width: 64px;
    height: 64px;
    min-width: 64px;
    border-radius: 6px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #ffffff;
    box-shadow: 0 4px 16px rgba(0, 0, 0, 0.35);
    transition: background var(--transition-fast);
  }

  .preview-meta {
    min-width: 0;
    flex: 1;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }

  .preview-badge {
    font-size: 10px;
    font-weight: 700;
    text-transform: uppercase;
    letter-spacing: 0.06em;
    color: var(--text-secondary);
  }

  .preview-title {
    font-size: 16px;
    font-weight: 700;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .preview-desc {
    font-size: 12px;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .form-section {
    display: flex;
    flex-direction: column;
    gap: 10px;
    padding: 12px;
    border-radius: var(--radius-md, 8px);
    background: var(--control-fill);
  }

  .form-field {
    display: flex;
    flex-direction: column;
    gap: 6px;
    min-width: 0;
  }

  label,
  .section-label {
    font-size: 11.5px;
    color: var(--text-secondary);
    font-weight: 500;
  }

  input {
    box-sizing: border-box;
    width: 100%;
    min-width: 0;
    height: 34px;
    padding: 6px 10px;
    background: var(--bg-tertiary);
    border: none;
    border-radius: var(--radius-sm);
    color: var(--text-primary);
    font: inherit;
    font-size: 13px;
    outline: none;
    transition: background var(--transition-fast);
  }

  input:hover,
  input:focus {
    background: var(--control-fill-hover);
  }

  input:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }

  .theme-grid {
    display: grid;
    grid-template-columns: repeat(8, minmax(0, 1fr));
    gap: 8px;
  }

  .theme-swatch {
    width: 100%;
    aspect-ratio: 1 / 1;
    border-radius: 50%;
    border: 2px solid transparent;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #ffffff;
    cursor: pointer;
    padding: 0;
    transition: transform 0.12s ease, border-color 0.12s ease;
  }

  .theme-swatch:hover {
    transform: scale(1.08);
  }

  .theme-swatch.active {
    border-color: var(--text-primary);
    transform: scale(1.08);
  }

  .theme-swatch:focus-visible,
  .icon-choice:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }

  .icon-grid {
    display: grid;
    grid-template-columns: repeat(8, minmax(0, 1fr));
    gap: 6px;
  }

  .icon-choice {
    height: 36px;
    border-radius: var(--radius-sm, 6px);
    border: 1px solid transparent;
    background: var(--bg-tertiary);
    color: var(--text-secondary);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    padding: 0;
    transition: background var(--transition-fast), color var(--transition-fast), border-color var(--transition-fast);
  }

  .icon-choice:hover {
    background: var(--control-fill-hover);
    color: var(--text-primary);
  }

  .icon-choice.active {
    background: color-mix(in srgb, var(--accent) 20%, transparent);
    border-color: var(--accent);
    color: var(--accent);
  }

  .form-error {
    margin: 0;
    color: var(--danger, #f87171);
    font-size: 12px;
  }

  .dialog-actions {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
    width: 100%;
  }

  @media (prefers-reduced-motion: reduce) {
    .preview-art,
    input,
    .theme-swatch,
    .icon-choice {
      transition: none;
    }
  }
</style>
