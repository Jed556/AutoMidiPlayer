<script lang="ts">
  import { onDestroy } from "svelte";
  import type {
    InstrumentConfig,
    InstrumentInputMode,
  } from "../../../../shared/types/games";
  import type { ArrangementState } from "../../../../shared/types/ipc";
  import {
    dispatchedGuitarPositions,
    guitarVoicing,
    guitarChord,
    type GuitarPosition,
  } from "../../utils/guitarLayout";
  import { PressHighlights } from "../../utils/pressHighlights";
  import { noteName } from "../../utils/instrumentKeyboard";
  import { t } from "../../i18n";

  let {
    instrument,
    activePitches,
    mode,
    performing,
    action,
    attacks,
    accessibleChords,
    previewPositions = [],
    previewChord = null,
  }: {
    instrument: InstrumentConfig;
    activePitches: Set<number>;
    mode: InstrumentInputMode;
    performing: boolean;
    action: ArrangementState | null;
    attacks: ArrangementState[];
    accessibleChords?: number;
    previewPositions?: GuitarPosition[];
    previewChord?: string | null;
  } = $props();
  const tuning = $derived(instrument.guitar?.tuning ?? []);
  const count = $derived(instrument.guitar?.fretCount ?? 24);
  const latest = $derived(attacks.at(-1) ?? null);
  const positions = $derived(
    performing
      ? dispatchedGuitarPositions(instrument, latest)
      : previewPositions.length > 0
        ? previewPositions
        : guitarVoicing(instrument, activePitches),
  );
  const chord = $derived(
    performing
      ? latest?.slot != null && latest.mode === "chords"
        ? instrument.chords?.[latest.slot]?.name
        : null
      : previewPositions.length > 0
        ? mode === "chords"
          ? previewChord
          : null
        : guitarChord(instrument, activePitches),
  );
  const strumArrow = $derived(
    performing && (latest?.mode === "chords" || latest?.mode === "frets")
      ? latest?.strum === "down"
        ? " ↓"
        : latest?.strum === "up"
          ? " ↑"
          : ""
      : "",
  );
  const timbreLabel = $derived(
    performing && latest?.timbre
      ? ` · ${t(`instrument.guitarTimbre_${latest.timbre}`)}`
      : "",
  );
  let previewHighlights = $state.raw<(GuitarPosition | string)[]>([]);
  const preview = new PressHighlights<GuitarPosition | string>(
    (value) =>
      typeof value === "string" ? value : `${value.string}/${value.fret}`,
    (values) => {
      previewHighlights = values;
    },
  );
  $effect(() => {
    instrument.id;
    mode;
    return () => preview.clear();
  });
  $effect(() => {
    if (performing) preview.clear();
    else preview.update(chord ? [...positions, chord] : positions);
  });
  onDestroy(() => preview.clear());
  const highlightedPositions = $derived(
    performing
      ? attacks
          .filter((attack) => attack.mode === mode)
          .flatMap((attack) => dispatchedGuitarPositions(instrument, attack))
      : previewHighlights.filter((value) => typeof value !== "string"),
  );
  const highlightedChords = $derived(
    new Set(
      attacks
        .filter((attack) => attack.mode === "chords")
        .map((attack) => attack.slot),
    ),
  );
  const activeCells = $derived(
    new Set(
      highlightedPositions.map(
        (position) => `${position.string}/${position.fret}`,
      ),
    ),
  );
  const activeFrets = $derived(
    new Map(positions.map((position) => [position.string, position.fret])),
  );
</script>

<section class="guitar-layout" aria-label={t("instrument.guitarView")}>
  <div class="guitar-heading">
    <span class="guitar-title"
      >{t(
        mode === "chords"
          ? "instrument.chordWheel"
          : mode === "frets"
            ? "instrument.fretboard"
            : "instrument.guitarView",
      )}</span
    >
    <span class="guitar-status" aria-live="polite"
      >{mode === "chords"
        ? chord
          ? `${chord}${strumArrow}${timbreLabel}`
          : t("instrument.guitarWaiting")
        : positions.length
          ? `${t("instrument.guitarFret", { fret: positions[0].fret })}${strumArrow}${timbreLabel}`
          : t("instrument.guitarWaiting")}</span
    >
  </div>
  {#if mode === "chords"}
    <div
      class="guitar-chords"
      aria-label={t("instrument.chordWheelAvailableChords")}
    >
      {#each instrument.chords?.slice(0, accessibleChords) ?? [] as item, index}<span
          class:active={performing
            ? highlightedChords.has(index)
            : previewHighlights.includes(item.name)}>{item.name}</span
        >{/each}
    </div>
  {/if}
  <div
    class="fret-scroll"
    tabindex="0"
    role="region"
    aria-label={t("instrument.fretboard")}
  >
    <div
      class="fret-grid"
      style:grid-template-columns={`48px repeat(${count + 1}, minmax(28px, 1fr))`}
    >
      <span class="string-header">{t("instrument.guitarString")}</span>
      {#each Array.from({ length: count + 1 }, (_, fret) => fret) as fret}<span
          class="fret-number"
          class:marked={[3, 5, 7, 9, 12, 15, 17, 19, 21, 24].includes(fret)}
          >{fret}</span
        >{/each}
      {#each tuning as open, string}
        <span class="string-label"
          >{string + 1}<small>{noteName(open)}</small></span
        >
        {#each Array.from({ length: count + 1 }, (_, fret) => fret) as fret}
          <span
            class="fret-cell"
            class:nut={fret === 0}
            class:active={activeCells.has(`${string}/${fret}`)}
            title={`${noteName(open + fret)} · ${t("instrument.guitarFret", { fret })}`}
          >
            {#if activeCells.has(`${string}/${fret}`)}<span class="finger"
                >{fret}</span
              >{/if}
          </span>
        {/each}
      {/each}
    </div>
  </div>
  <div class="tab-strip" aria-label={t("instrument.guitarTabPreview")}>
    <span class="tab-title">{t("instrument.guitarTabPreview")}</span>
    {#each tuning as open, string}<span class="tab-string"
        ><small>{noteName(open)}</small><b>{activeFrets.get(string) ?? "-"}</b
        ></span
      >{/each}
  </div>
  {#if action && (action.omittedMelody || action.omittedAccompaniment)}
    <p class="guitar-hint">
      {t("instrument.guitarOmittedNotes", {
        melody: action.omittedMelody,
        accompaniment: action.omittedAccompaniment,
      })}
    </p>
  {:else if mode === "frets" && !performing && activePitches.size && !positions.length}
    <p class="guitar-hint">{t("instrument.guitarUnplayable")}</p>
  {/if}
</section>

<style>
  .guitar-layout {
    width: 100%;
    max-width: 1440px;
    margin: 0 auto;
    padding: 6px 0 4px;
  }
  .guitar-heading {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: 16px;
    margin-bottom: 10px;
  }
  .guitar-title {
    color: var(--text-primary);
    font-size: 14px;
    font-weight: 600;
  }
  .guitar-status {
    font-size: 12px;
    color: var(--text-secondary);
    font-variant-numeric: tabular-nums;
  }
  .guitar-chords {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 16px;
  }
  .guitar-chords > span {
    padding: 5px 9px;
    border-radius: var(--radius-sm);
    color: var(--text-secondary);
    background: var(--bg-tertiary);
    font-size: 12px;
  }
  .guitar-chords > span.active {
    background: var(--accent);
    color: white;
  }
  .fret-scroll {
    width: 100%;
    overflow-x: auto;
    padding-bottom: 8px;
  }
  .fret-scroll:focus-visible {
    outline: 2px solid var(--accent);
    outline-offset: 3px;
  }
  .fret-grid {
    display: grid;
    min-width: 760px;
  }
  .fret-number,
  .string-header {
    padding: 4px 0 8px;
    text-align: center;
    font-size: 10px;
    color: var(--text-tertiary);
  }
  .fret-number.marked {
    color: var(--text-primary);
    font-weight: 600;
  }
  .string-header {
    text-align: left;
  }
  .string-label {
    display: flex;
    align-items: center;
    gap: 5px;
    font-size: 11px;
    color: var(--text-primary);
  }
  .string-label small {
    color: var(--text-tertiary);
    font-size: 9px;
  }
  .fret-cell {
    position: relative;
    display: grid;
    place-items: center;
    height: 36px;
    border-right: 1px solid var(--border-subtle);
  }
  .fret-cell::before {
    content: "";
    position: absolute;
    left: 0;
    right: 0;
    height: 1px;
    background: var(--text-tertiary);
    opacity: 0.4;
  }
  .fret-cell.nut {
    border-right: 3px solid var(--text-secondary);
  }
  .finger {
    position: relative;
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    background: var(--accent);
    color: white;
    border-radius: 50%;
    font-size: 10px;
    font-weight: 600;
  }
  .tab-strip {
    display: flex;
    align-items: center;
    gap: 16px;
    margin-top: 14px;
    padding-top: 12px;
    border-top: 1px solid var(--border-subtle);
  }
  .tab-title {
    margin-right: auto;
    font-size: 11px;
    color: var(--text-secondary);
  }
  .tab-string {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 4px;
    min-width: 22px;
    font-family: var(--font-mono, monospace);
  }
  .tab-string small {
    font-size: 9px;
    color: var(--text-tertiary);
  }
  .tab-string b {
    font-size: 13px;
    font-weight: 500;
    color: var(--text-primary);
  }
  .guitar-hint {
    margin: 14px 0 0;
    font-size: 11px;
    color: var(--text-tertiary);
  }
  @media (max-width: 600px) {
    .tab-strip {
      gap: 8px;
    }
    .guitar-heading {
      align-items: flex-start;
    }
  }
</style>
