<script lang="ts">
  import { ICONS, type IconName } from './iconData.js';
  import RefreshIcon from './RefreshIcon.svelte';
  import TransposeIcon from './TransposeIcon.svelte';
  import VolumeIcon from './VolumeIcon.svelte';
  import SpeedIcon from './SpeedIcon.svelte';
  import HeadphonesIcon from './HeadphonesIcon.svelte';
  import ShuffleIcon from './ShuffleIcon.svelte';
  import PlayPauseIcon from './PlayPauseIcon.svelte';
  import LoopIcon from './LoopIcon.svelte';
  import ThemeToggleIcon from './ThemeToggleIcon.svelte';
  import WindowCloseIcon from './WindowCloseIcon.svelte';
  import WindowMinimizeIcon from './WindowMinimizeIcon.svelte';
  import WindowMaximizeIcon from './WindowMaximizeIcon.svelte';

  interface Props {
    name: IconName | string;
    size?: number;
    filled?: boolean;
    active?: boolean;
    hovered?: boolean;
    maximized?: boolean;
    playing?: boolean;
    isDark?: boolean;
    class?: string;
    style?: string;
    title?: string;
    loading?: boolean;
    duration?: number;
    speed?: number;
    volume?: number;
    trigger?: number | boolean;
  }

  let {
    name,
    size = 20,
    filled = false,
    active = false,
    hovered = false,
    maximized = false,
    playing,
    isDark,
    class: className = '',
    style = '',
    title,
    loading,
    duration,
    speed,
    volume,
    trigger
  }: Props = $props();

  let isPlayPauseVariant = $derived.by(() => {
    const raw = String(name || '').toLowerCase();
    return (
      raw === 'playpause' ||
      raw === 'play_pause' ||
      raw === 'playpauseicon' ||
      raw === 'play' ||
      raw === 'pause'
    );
  });

  let effectivePlayPausePlaying = $derived.by(() => {
    if (playing !== undefined) return Boolean(playing);
    if (active) return true;
    return String(name || '').toLowerCase() === 'pause';
  });

  let isTransposeVariant = $derived.by(() => {
    const raw = String(name || '');
    return (
      raw === 'Transpose' ||
      raw === 'TransposeSmart' ||
      raw === 'TransposeIgnore' ||
      raw === 'TransposeUp' ||
      raw === 'TransposeDown'
    );
  });

  let isSpeedVariant = $derived.by(() => {
    const raw = String(name || '');
    const clean = raw.replace(/\d+$/, '').replace(/_(regular|filled)$/i, '');
    return raw === 'TopSpeed' || raw === 'Speed' || clean === 'TopSpeed' || clean === 'Speed';
  });

  let isHeadphonesVariant = $derived.by(() => {
    const raw = String(name || '');
    const base = raw.replace(/\d+$/, '').replace(/_(regular|filled)$/i, '');
    return raw === 'Headphones' || base === 'Headphones';
  });

  let isHeadphonesFilled = $derived.by(() => {
    const raw = String(name || '');
    return filled || raw.endsWith('_filled') || raw.endsWith('Filled');
  });

  let isShuffleVariant = $derived.by(() => {
    const raw = String(name || '').replace(/^(local|media):/i, '').toLowerCase();
    return raw === 'shuffle';
  });

  let isLoopVariant = $derived.by(() => {
    const raw = String(name || '');
    const clean = raw
      .replace(/_(regular|filled)$/i, '')
      .replace(/Filled$/i, '')
      .replace(/^local:/i, '')
      .replace(/^media:/i, '');
    const lower = clean.toLowerCase();
    return (
      clean === 'Repeat' ||
      clean === 'RepeatOne' ||
      lower === 'repeat' ||
      lower === 'repeatone' ||
      lower === 'loop' ||
      lower === 'loopicon'
    );
  });

  let isRefreshVariant = $derived.by(() => {
    const raw = String(name || '');
    const base = raw.replace(/\d+$/, '').replace(/_(regular|filled)$/i, '');
    return (
      raw === 'ArrowClockwise' ||
      base === 'ArrowClockwise' ||
      raw === 'ArrowReset' ||
      base === 'ArrowReset' ||
      raw.toLowerCase() === 'refresh'
    );
  });

  let isThemeVariant = $derived.by(() => {
    const raw = String(name || '').toLowerCase();
    const clean = raw.replace(/\d+$/, '').replace(/_(regular|filled)$/i, '');
    return (
      raw === 'themetoggle' ||
      clean === 'themetoggle' ||
      raw === 'themetoggleicon' ||
      raw === 'daynight' ||
      clean === 'daynight' ||
      raw === 'weathermoon' ||
      clean === 'weathermoon' ||
      raw === 'weathersunny' ||
      clean === 'weathersunny' ||
      raw === 'moon' ||
      raw === 'sun'
    );
  });

  let resolved = $derived.by(() => {
    const raw = String(name || '').trim();
    const isExplicitFilled = filled || raw.endsWith('_filled') || raw.endsWith('Filled');

    // Clean up name by removing suffixes/prefixes
    const clean = raw
      .replace(/_(regular|filled)$/i, '')
      .replace(/Filled$/i, '')
      .replace(/^local:/i, '')
      .replace(/^media:/i, '');

    // Strip size numbers if present (e.g. "Add16" -> "Add", "Eye24" -> "Eye", "Delete16" -> "Delete")
    const base = clean.replace(/\d+$/, '');

    // Lookup icon definition
    const iconDef = ICONS[clean] || ICONS[base];

    if (iconDef) {
      if (isExplicitFilled && iconDef.filledPaths && iconDef.filledPaths.length > 0) {
        return {
          viewBox: iconDef.filledViewBox || iconDef.viewBox,
          paths: iconDef.filledPaths
        };
      }
      return {
        viewBox: iconDef.viewBox,
        paths: iconDef.paths
      };
    }

    // Case-insensitive lookup fallback
    const lowerClean = clean.toLowerCase();
    const lowerBase = base.toLowerCase();
    for (const [k, v] of Object.entries(ICONS)) {
      const lowerK = k.toLowerCase();
      if (lowerK === lowerClean || lowerK === lowerBase) {
        if (isExplicitFilled && v.filledPaths && v.filledPaths.length > 0) {
          return {
            viewBox: v.filledViewBox || v.viewBox,
            paths: v.filledPaths
          };
        }
        return {
          viewBox: v.viewBox,
          paths: v.paths
        };
      }
    }

    // Default fallback: Info icon
    const fallback = ICONS['Info'];
    return fallback
      ? { viewBox: fallback.viewBox, paths: fallback.paths }
      : { viewBox: '0 0 24 24', paths: [] };
  });
</script>

{#if name === 'WindowClose'}
  <WindowCloseIcon {hovered} {trigger} {size} class={className} {style} {title} {duration} />
{:else if name === 'WindowMinimize'}
  <WindowMinimizeIcon {hovered} {trigger} {size} class={className} {style} {title} {duration} />
{:else if name === 'WindowMaximize'}
  <WindowMaximizeIcon {maximized} {hovered} {trigger} {size} class={className} {style} {title} {duration} />
{:else if (loading !== undefined || trigger !== undefined) && isRefreshVariant}
  <RefreshIcon loading={loading ?? false} {trigger} {size} class={className} {style} {title} {duration} />
{:else if isTransposeVariant}
  <TransposeIcon mode={name} {size} class={className} {style} {title} {duration} />
{:else if /^Volume[0-3]$/.test(String(name))}
  <VolumeIcon volume={volume ?? ({ Volume0: 0, Volume1: 0.2, Volume2: 0.5, Volume3: 1 }[String(name)] ?? 1)}
    {size} class={className} {style} {title} {duration} />
{:else if isSpeedVariant}
  <SpeedIcon speed={speed ?? 1.0} {size} class={className} {style} {title} {duration} />
{:else if isHeadphonesVariant}
  <HeadphonesIcon
    filled={isHeadphonesFilled}
    {active}
    {size}
    class={className}
    {style}
    {title}
    {duration}
    {trigger}
  />
{:else if isShuffleVariant}
  <ShuffleIcon
    active={active || filled}
    {size}
    class={className}
    {style}
    {title}
    {duration}
    {trigger}
  />
{:else if isPlayPauseVariant}
  <PlayPauseIcon
    playing={effectivePlayPausePlaying}
    {size}
    class={className}
    {style}
    {title}
    {duration}
    {trigger}
  />
{:else if isLoopVariant}
  <LoopIcon
    mode={String(name).toLowerCase().includes('one') ? 'single' : (active ? 'all' : 'off')}
    {active}
    {size}
    class={className}
    {style}
    {title}
    {duration}
    {loading}
    {trigger}
  />
{:else if isThemeVariant}
  <ThemeToggleIcon
    isDark={isDark !== undefined ? isDark : (String(name).toLowerCase().includes('sunny') || String(name).toLowerCase() === 'sun' ? false : true)}
    {active}
    {filled}
    {size}
    class={className}
    {style}
    {title}
    {duration}
    {trigger}
  />
{:else}
  <svg
    xmlns="http://www.w3.org/2000/svg"
    viewBox={resolved.viewBox}
    width={size}
    height={size}
    class="fluent-system-icon {className}"
    {style}
    fill="currentColor"
    role="img"
    aria-hidden={title ? undefined : 'true'}
  >
    {#if title}
      <title>{title}</title>
    {/if}
    {#each resolved.paths as d}
      <path {d} />
    {/each}
  </svg>
{/if}

<style>
  .fluent-system-icon {
    display: inline-block;
    vertical-align: middle;
    flex-shrink: 0;
  }
</style>
