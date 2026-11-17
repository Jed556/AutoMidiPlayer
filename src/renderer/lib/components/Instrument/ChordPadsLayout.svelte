<script lang="ts">
  import type { ChordDefinition } from '../../../../shared/types/games';
  import { t } from '../../i18n';
  let { chords, bindings, activePitches, onpreview }: {
    chords: ChordDefinition[]; bindings: string[]; activePitches: Set<number>;
    onpreview: (pitches: number[]) => void;
  } = $props();
  const mask = $derived([...activePitches].reduce((value, pitch) => value | (1 << (pitch % 12)), 0));
</script>

<div class="chord-pads" role="group" aria-label={t('instrument.guitarMode_chords')}>
  {#each chords as chord, index}
    <button type="button" class:active={mask !== 0 && chord.pitchClasses.reduce((value, pitch) => value | (1 << pitch), 0) === mask}
      disabled={!bindings[index]} aria-label={`${chord.name}${bindings[index] ? ` (${bindings[index]})` : ''}`}
      onclick={() => onpreview(chord.pitchClasses.map(pitch => 60 + pitch))}>
      <span class="chord-name">{chord.name}</span><kbd>{bindings[index] ?? ''}</kbd>
    </button>
  {/each}
</div>

<style>
  .chord-pads { display: grid; grid-template-columns: repeat(auto-fit, minmax(72px, 1fr)); gap: 10px; width: 100%; max-width: 720px; }
  button { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 22px 10px; border: 1px solid var(--border-subtle); border-radius: var(--radius-md); background: var(--bg-card); color: var(--text-primary); cursor: pointer; font: inherit; transition: transform var(--transition-fast), background var(--transition-fast); }
  button:hover { background: var(--bg-tertiary); }
  button:active { transform: translateY(2px); }
  button:focus-visible { outline: 2px solid var(--accent); outline-offset: 3px; }
  button.active { background: var(--accent); color: white; }
  button:disabled { opacity: .45; cursor: default; }
  .chord-name { font-size: 22px; font-weight: 600; }
  kbd { font: 12px var(--font-mono, monospace); opacity: .7; }
</style>
