<script lang="ts">
  import { onMount } from 'svelte';
  import { createFrameResizeObserver } from '../utils/frameResizeObserver';
  import { uiStore } from '../state/uiStore.svelte';
  import { settingsStore } from '../state/settingsStore.svelte';
  import FluentIcon from '../components/Common/FluentIcon.svelte';
  import CustomScrollViewer from '../components/Common/CustomScrollViewer.svelte';
  import { t } from '../i18n';
  import {
    ABOUT_LINKS,
    INITIAL_CONTRIBUTORS,
    loadOpenSourceLicenses,
    type ContributorInfo,
    type LicenseInfo
  } from '../data/aboutData';



  let licenses = $state<LicenseInfo[]>([]);

  onMount(() => {
    void loadOpenSourceLicenses().then(items => { licenses = items; });
  });

  let contributors = $state<ContributorInfo[]>([...INITIAL_CONTRIBUTORS]);
  let viewEl = $state<HTMLDivElement>();
  let containerEl = $state<HTMLElement | null>(null);
  let isCentered = $state(false);

  function checkFit() {
    if (viewEl && containerEl) {
      // Only center vertically when the viewport height exceeds content height with comfortable breathing room
      isCentered = viewEl.clientHeight >= containerEl.scrollHeight + 32;
    }
  }

  $effect(() => {
    if (!viewEl || !containerEl) return;
    const ro = createFrameResizeObserver(() => {
      checkFit();
    });
    ro.observe(viewEl);
    ro.observe(containerEl);
    checkFit();
    return () => ro.disconnect();
  });

  onMount(async () => {
    try {
      const res = await fetch('https://api.github.com/repos/Jed556/AutoMidiPlayer/contributors', {
        headers: { 'User-Agent': 'AutoMidiPlayer' }
      });
      if (res.ok) {
        const data = await res.json();
        if (Array.isArray(data)) {
          const filtered = data
            .filter((c: any) => !c.login.includes('[bot]'))
            .map((c: any) => ({
              name: c.login,
              avatarUrl: c.avatar_url,
              profileUrl: c.html_url
            }));
          if (filtered.length > 0) {
            contributors = filtered;
          }
        }
      }
    } catch {
      // Retain INITIAL_CONTRIBUTORS on offline or network issue
    }
  });

  function openLicense(license: LicenseInfo) {
    uiStore.openDialog('licenses', license);
  }
</script>

<CustomScrollViewer
  bind:scrollContainer={viewEl}
  class="about-scroll-viewer"
  contentClass="about-view {isCentered ? 'is-centered' : ''}"
  fadeHeight={36}
>
  <div class="about-container" bind:this={containerEl}>
    <!-- Header Brand Section (Centered) -->
    <header class="about-header">
      <img src="./logo-fill.svg" alt="Auto MIDI Player" class="about-logo" />
      <h1 class="about-title">{t('about.appName')}</h1>
      <div class="about-version">
        <span>{t('about.versionPrefix')}{settingsStore.programVersion}</span>
      </div>
      <p class="about-tagline">
        {t('about.tagline')}
      </p>
      <div class="about-support">
        <span>{t('about.supportPrefix')}</span>
        <a
          href="https://github.com/Jed556/AutoMidiPlayer/blob/main/CONTRIBUTING.md"
          target="_blank"
          rel="noreferrer"
          class="accent-link"
        >
          {t('about.contributing')}
        </a>
        <span>{t('about.supportMid')}</span>
        <a
          href="https://github.com/Jed556/AutoMidiPlayer"
          target="_blank"
          rel="noreferrer"
          class="accent-link"
        >
          {t('about.github')}
        </a>
        <span>{t('about.supportSuffix')}</span>
      </div>
    </header>

    <!-- Links Section -->
    <section class="about-section">
      <h2 class="section-title">{t('about.links')}</h2>
      <div class="links-row">
        {#each ABOUT_LINKS as item}
          <a
            href={item.url}
            target="_blank"
            rel="noreferrer"
            class="action-button"
          >
            <FluentIcon name={item.icon} size={18} class="btn-icon" />
            <span class="btn-text">{item.name}</span>
          </a>
        {/each}
      </div>
    </section>

    <!-- Special Thanks Section -->
    <section class="about-section">
      <h2 class="section-title">{t('about.specialThanks')}</h2>
      <p class="fork-text">
        <span>{t('about.forkPrefix')}</span>
        <a
          href="https://github.com/sabihoshi/GenshinLyreMidiPlayer"
          target="_blank"
          rel="noreferrer"
          class="accent-link"
        >
          {t('about.forkRepo')}
        </a>
        <span>{t('about.forkSuffix')}</span>
      </p>

      <h3 class="contributors-title">{t('about.contributors')}</h3>
      <div class="contributors-row">
        {#each contributors as person (person.name)}
          <a
            href={person.profileUrl}
            target="_blank"
            rel="noreferrer"
            class="contributor-pill"
            title={person.name}
          >
            <div class="avatar-box">
              <img src={person.avatarUrl} alt={person.name} class="avatar-img" />
            </div>
            <div class="name-reveal">
              <span class="contributor-name">{person.name}</span>
            </div>
          </a>
        {/each}
      </div>
    </section>

    <!-- Open Source Licenses Section -->
    <section class="about-section">
      <h2 class="section-title">{t('about.openSourceLicenses')}</h2>
      <div class="licenses-grid">
        {#each licenses as lic (`${lic.name}@${lic.version}`)}
          <button
            type="button"
            class="license-chip"
            onclick={() => openLicense(lic)}
            title="Click to view license details"
          >
            <div class="chip-top">
              <span class="chip-name">{lic.name}</span>
              {#if lic.version}
                <span class="chip-version">{lic.version}</span>
              {/if}
            </div>
            <div class="chip-bottom">
              <span class="chip-license">{lic.licenseName}</span>
            </div>
          </button>
        {/each}
      </div>

      <p class="disclaimer-text">
        {t('about.disclaimer')}
      </p>
    </section>
  </div>
</CustomScrollViewer>

<style>
  :global(.about-scroll-viewer) {
    height: 100%;
    width: 100%;
  }

  :global(.about-scroll-viewer .about-view) {
    display: flex;
    flex-direction: column;
    min-height: 100%;
    width: 100%;
    background: transparent;
    box-sizing: border-box;
  }

  .about-container {
    width: 100%;
    max-width: 960px;
    margin: 0 auto;
    padding: 24px 32px 32px 32px;
    display: flex;
    flex-direction: column;
    gap: 20px;
    box-sizing: border-box;
  }

  /* Only center vertically when the screen is big enough that all content fits comfortably without clipping */
  :global(.about-scroll-viewer .about-view.is-centered) .about-container {
    margin: auto auto;
  }

  /* Centered Header */
  .about-header {
    display: flex;
    flex-direction: column;
    align-items: center;
    text-align: center;
    margin: 16px auto 4px auto;
    max-width: 740px;
  }

  .about-logo {
    width: 64px;
    height: 64px;
    object-fit: contain;
    margin-bottom: 8px;
    user-select: none;
  }

  .about-title {
    font-size: 26px;
    font-weight: 700;
    color: var(--text-primary);
    margin: 0;
    line-height: 1.25;
  }

  .about-version {
    font-size: 13.5px;
    color: var(--text-primary);
    opacity: 0.65;
    margin-top: 4px;
  }

  .about-tagline {
    font-size: 14px;
    line-height: 1.6;
    color: var(--text-primary);
    opacity: 0.85;
    margin: 16px 0 0 0;
    text-align: center;
  }

  .about-support {
    font-size: 14px;
    color: var(--text-secondary);
    margin-top: 8px;
    text-align: center;
    line-height: 1.6;
  }

  .accent-link {
    color: var(--accent, #1DB954);
    text-decoration: none;
    transition: opacity var(--transition-fast);
  }

  .accent-link:hover {
    text-decoration: underline;
    opacity: 0.9;
  }

  /* Sections */
  .about-section {
    display: flex;
    flex-direction: column;
    width: 100%;
  }

  .section-title {
    font-size: 20px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 0 0 14px 0;
    letter-spacing: -0.01em;
  }

  /* Links Row */
  .links-row {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 12px;
  }

  @media (max-width: 860px) {
    .links-row {
      grid-template-columns: repeat(2, 1fr);
    }
  }

  @media (max-width: 480px) {
    .links-row {
      grid-template-columns: 1fr;
    }
  }

  .action-button {
    width: 100%;
    padding: 10px 16px;
    background: var(--control-fill, rgba(255, 255, 255, 0.05));
    border: none;
    border-radius: 4px;
    color: var(--text-primary);
    text-decoration: none;
    font-size: 14px;
    display: inline-flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
    box-sizing: border-box;
    transition: background 0.16s ease;
  }

  .action-button:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
  }

  .action-button:active {
    background: rgba(255, 255, 255, 0.04);
  }

  .action-button :global(.btn-icon) {
    color: var(--text-primary);
    opacity: 0.9;
    flex-shrink: 0;
  }

  .btn-text {
    vertical-align: middle;
    font-size: 14px;
    font-weight: 500;
  }

  /* Special Thanks */
  .fork-text {
    font-size: 14px;
    line-height: 1.65;
    color: var(--text-primary);
    opacity: 0.9;
    margin: 0;
  }

  .contributors-title {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
    margin: 16px 0 10px 0;
  }

  .contributors-row {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
    align-items: center;
  }

  /* Contributor Avatar Expandable Pill (Faithful to WPF ContributorButtonTemplate) */
  .contributor-pill {
    display: inline-flex;
    align-items: center;
    border-radius: 20px;
    background: var(--control-fill, rgba(255, 255, 255, 0.05));
    border: none;
    padding: 4px;
    text-decoration: none;
    cursor: pointer;
    color: var(--text-primary);
    transition: background 0.25s cubic-bezier(0.645, 0.045, 0.355, 1);
  }

  .contributor-pill:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.09));
  }

  .avatar-box {
    width: 32px;
    height: 32px;
    border-radius: 16px;
    background: var(--bg-tertiary, rgba(255, 255, 255, 0.08));
    border: none;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    flex-shrink: 0;
  }

  .avatar-img {
    width: 32px;
    height: 32px;
    border-radius: 16px;
    object-fit: cover;
    border: none;
    display: block;
  }

  .name-reveal {
    max-width: 0;
    opacity: 0;
    overflow: hidden;
    white-space: nowrap;
    transition: max-width 0.25s cubic-bezier(0.645, 0.045, 0.355, 1),
                opacity 0.25s cubic-bezier(0.645, 0.045, 0.355, 1),
                margin 0.25s cubic-bezier(0.645, 0.045, 0.355, 1);
  }

  .contributor-pill:hover .name-reveal {
    max-width: 140px;
    opacity: 1;
    margin: 0 12px;
  }

  .contributor-name {
    font-size: 14px;
    font-weight: 500;
    color: var(--text-primary);
    line-height: 1;
    display: inline-block;
  }

  /* Licenses Grid (Faithful to WPF LicenseChipButtonStyle) */
  .licenses-grid {
    display: grid;
    grid-template-columns: repeat(4, 1fr);
    gap: 12px;
    margin-top: 2px;
  }

  @media (max-width: 860px) {
    .licenses-grid {
      grid-template-columns: repeat(2, 1fr);
    }
  }

  @media (max-width: 480px) {
    .licenses-grid {
      grid-template-columns: 1fr;
    }
  }

  .license-chip {
    width: 100%;
    padding: 14px 18px;
    background: var(--control-fill, rgba(255, 255, 255, 0.05));
    border: none;
    border-radius: 4px;
    color: var(--text-primary);
    text-align: left;
    display: flex;
    flex-direction: column;
    gap: 4px;
    cursor: pointer;
    box-sizing: border-box;
    transition: background 0.16s ease;
  }

  .license-chip:hover {
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
  }

  .license-chip:active {
    background: rgba(255, 255, 255, 0.03);
  }

  .chip-top {
    display: flex;
    align-items: baseline;
    flex-wrap: wrap;
  }

  .chip-name {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
    margin-right: 8px;
  }

  .chip-version {
    font-size: 13px;
    opacity: 0.65;
    color: var(--text-secondary);
  }

  .chip-bottom {
    display: flex;
    align-items: center;
  }

  .chip-license {
    font-size: 13px;
    opacity: 0.75;
    color: var(--text-secondary);
  }

  /* Disclaimer Text */
  .disclaimer-text {
    font-size: 12.5px;
    line-height: 1.6;
    color: var(--text-secondary);
    opacity: 0.75;
    margin: 14px 0 0 0;
  }
</style>
