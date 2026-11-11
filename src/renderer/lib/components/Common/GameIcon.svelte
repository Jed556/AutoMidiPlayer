<script lang="ts">
  import { onDestroy } from 'svelte';
  import { gameStore } from '../../state/gameStore.svelte';
  import FluentIcon from '../Icons/FluentIcon.svelte';

  interface Props {
    size?: number | string;
    borderRadius?: number | string;
    objectFit?: 'cover' | 'contain';
    running?: boolean;
    placeholderSize?: number;
    class?: string;
    GrayscaleInactiveGame?: boolean;
    grayscaleInactiveGame?: boolean;
  }

  let {
    size = '100%',
    borderRadius = 'inherit',
    objectFit = 'cover',
    running = undefined,
    placeholderSize = 18,
    class: className = '',
    GrayscaleInactiveGame = true,
    grayscaleInactiveGame = undefined,
  }: Props = $props();

  let shouldGrayscale = $derived(
    grayscaleInactiveGame !== undefined
      ? grayscaleInactiveGame
      : (GrayscaleInactiveGame ?? true)
  );

  interface GameSnapshot {
    id: string;
    iconUrl?: string;
    imageFileName?: string;
    displayName?: string;
    name?: string;
    isMissing?: boolean;
    isRunning?: boolean;
  }

  let currentSrc = $state<string>('');
  let prevSrc = $state<string>('');
  let currentHasError = $state(false);
  let prevHasError = $state(false);

  let previousGame = $state<GameSnapshot | null>(null);
  let isSweeping = $state(false);
  let lastGameId = $state<string | null>(null);
  let transitionRevision = 0;
  let sweepTimer: ReturnType<typeof setTimeout> | null = null;

  function getGameImageCandidates(game: GameSnapshot | typeof gameStore.activeGame): string[] {
    if (!game) return ['images/games/Genshin_Impact.png'];
    const candidates: string[] = [];
    if (game.iconUrl) candidates.push(game.iconUrl);
    if (game.imageFileName) candidates.push(`images/games/${game.imageFileName}`);
    candidates.push('images/games/Genshin_Impact.png');
    return [...new Set(candidates)];
  }

  function loadFirstValidImage(candidates: string[]): Promise<string> {
    return new Promise((resolve) => {
      let index = 0;
      function tryNext() {
        if (index >= candidates.length) {
          resolve('images/games/Genshin_Impact.png');
          return;
        }
        const src = candidates[index++];
        const img = new Image();
        img.onload = () => {
          img.decode().then(() => resolve(src)).catch(() => resolve(src));
        };
        img.onerror = () => {
          tryNext();
        };
        img.src = src;
        if (img.complete && img.naturalWidth > 0) {
          img.decode().then(() => resolve(src)).catch(() => resolve(src));
        }
      }
      tryNext();
    });
  }

  // Preload all registered game icons into browser cache
  $effect(() => {
    if (gameStore.games && gameStore.games.length > 0) {
      for (const game of gameStore.games) {
        const candidates = getGameImageCandidates(game);
        for (const src of candidates) {
          const img = new Image();
          img.src = src;
        }
      }
    }
  });

  $effect(() => {
    const current = gameStore.activeGame;
    const currentId = current?.id ?? null;

    if (!currentId) return;

    if (lastGameId === null) {
      // Initial mount: load immediately without sweep
      lastGameId = currentId;
      const candidates = getGameImageCandidates(current);
      void loadFirstValidImage(candidates).then(src => {
        currentSrc = src;
        currentHasError = false;
      });
      return;
    }

    if (currentId !== lastGameId) {
      const prevId = lastGameId;
      lastGameId = currentId;
      const rev = ++transitionRevision;

      const prev = gameStore.games.find(g => g.id === prevId);
      const snapshot: GameSnapshot = {
        id: prev?.id ?? prevId,
        iconUrl: prev?.iconUrl,
        imageFileName: prev?.imageFileName,
        displayName: prev?.displayName,
        name: prev?.name,
        isMissing: prev?.isMissing,
        isRunning: prev?.isRunning,
      };

      const candidates = getGameImageCandidates(current);

      // Preload & decode incoming image before starting the animation
      void loadFirstValidImage(candidates).then(nextSrc => {
        if (rev !== transitionRevision) return;

        previousGame = snapshot;
        prevSrc = currentSrc;
        prevHasError = currentHasError;

        currentSrc = nextSrc;
        currentHasError = false;

        if (sweepTimer) clearTimeout(sweepTimer);
        isSweeping = false;

        requestAnimationFrame(() => {
          if (rev !== transitionRevision) return;
          isSweeping = true;

          sweepTimer = setTimeout(() => {
            if (rev === transitionRevision) {
              isSweeping = false;
              previousGame = null;
              sweepTimer = null;
            }
          }, 550);
        });
      });
    }
  });

  onDestroy(() => {
    if (sweepTimer) clearTimeout(sweepTimer);
  });

  let isRunningResolved = $derived(running !== undefined ? running : Boolean(gameStore.activeGame?.isRunning));
  let currentDisplayName = $derived(
    gameStore.activeGame?.displayName || gameStore.activeGame?.name || 'Game'
  );
  let currentIsMissing = $derived(Boolean(gameStore.activeGame?.isMissing));

  let sizeStyle = $derived(
    typeof size === 'number' ? `${size}px` : size
  );
  let radiusStyle = $derived(
    typeof borderRadius === 'number' ? `${borderRadius}px` : borderRadius
  );
</script>

<div
  class="game-icon-container {className}"
  class:running={isRunningResolved}
  class:grayscale-inactive={shouldGrayscale}
  class:no-grayscale={!shouldGrayscale}
  style:width={sizeStyle}
  style:height={sizeStyle}
  style:border-radius={radiusStyle}
>
  {#if isSweeping && previousGame}
    <!-- Outgoing Previous Icon: Masked OUT diagonally from upper-left to lower-right -->
    <div class="game-icon-layer game-icon-prev sweeping">
      {#if !prevHasError && !previousGame.isMissing && prevSrc}
        <img
          class="game-icon-img"
          class:running={previousGame.isRunning}
          style:object-fit={objectFit}
          src={prevSrc}
          alt={previousGame.displayName || previousGame.name || 'Game'}
          draggable="false"
        />
      {:else}
        <div class="game-icon-placeholder" class:running={previousGame.isRunning}>
          <FluentIcon name="Games" size={placeholderSize} />
        </div>
      {/if}
    </div>
  {/if}

  <!-- Incoming Current Icon: Masked IN diagonally from upper-left to lower-right -->
  <div class="game-icon-layer game-icon-curr" class:sweeping={isSweeping}>
    {#if !currentHasError && !currentIsMissing && currentSrc}
      <img
        class="game-icon-img"
        class:running={isRunningResolved}
        style:object-fit={objectFit}
        src={currentSrc}
        alt={currentDisplayName}
        draggable="false"
        onerror={() => { currentHasError = true; }}
      />
    {:else}
      <div class="game-icon-placeholder" class:running={isRunningResolved}>
        <FluentIcon name="Games" size={placeholderSize} />
      </div>
    {/if}
  </div>
</div>

<style>
  .game-icon-container {
    position: relative;
    overflow: hidden;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
  }

  .game-icon-layer {
    position: relative;
    width: 100%;
    height: 100%;
    border-radius: inherit;
    overflow: hidden;
  }

  .game-icon-img {
    width: 100%;
    height: 100%;
    border-radius: inherit;
    filter: grayscale(1);
    transition: filter var(--transition-normal);
    display: block;
    user-select: none;
  }

  .game-icon-container.running .game-icon-img,
  .game-icon-img.running,
  .game-icon-container.no-grayscale .game-icon-img,
  .game-icon-container:not(.grayscale-inactive) .game-icon-img {
    filter: none;
  }

  .game-icon-placeholder {
    width: 100%;
    height: 100%;
    border-radius: inherit;
    background: var(--bg-hover, rgba(255, 255, 255, 0.08));
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-tertiary);
    transition: color var(--transition-normal);
  }

  .game-icon-container.running .game-icon-placeholder,
  .game-icon-placeholder.running,
  .game-icon-container.no-grayscale .game-icon-placeholder,
  .game-icon-container:not(.grayscale-inactive) .game-icon-placeholder {
    color: var(--accent);
  }

  /* DUAL-MASK SWEEP (45-degree angle from upper-left to lower-right) */

  /* OUTGOING (PREVIOUS) - masks OUT from top-left to bottom-right (complementary to incoming) */
  .game-icon-layer.game-icon-prev.sweeping {
    position: absolute;
    inset: 0;
    z-index: 1;
    pointer-events: none;
    -webkit-mask-image: linear-gradient(
      135deg,
      transparent 0%,
      transparent 42%,
      rgba(0, 0, 0, 0.5) 50%,
      #000 55%,
      #000 100%
    );
    mask-image: linear-gradient(
      135deg,
      transparent 0%,
      transparent 42%,
      rgba(0, 0, 0, 0.5) 50%,
      #000 55%,
      #000 100%
    );
    -webkit-mask-size: 320% 320%;
    mask-size: 320% 320%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
    -webkit-mask-position: 100% 100%;
    mask-position: 100% 100%;
    will-change: mask-position, -webkit-mask-position;
    animation: sweepMaskOut 0.55s cubic-bezier(0.25, 1, 0.5, 1) forwards;
  }

  @keyframes sweepMaskOut {
    0% {
      -webkit-mask-position: 100% 100%;
      mask-position: 100% 100%;
    }
    100% {
      -webkit-mask-position: 0% 0%;
      mask-position: 0% 0%;
    }
  }

  /* INCOMING (CURRENT) - reveals from top-left to bottom-right */
  .game-icon-layer.game-icon-curr.sweeping {
    position: absolute;
    inset: 0;
    z-index: 2;
    -webkit-mask-image: linear-gradient(
      135deg,
      #000 0%,
      #000 45%,
      rgba(0, 0, 0, 0.5) 50%,
      transparent 58%,
      transparent 100%
    );
    mask-image: linear-gradient(
      135deg,
      #000 0%,
      #000 45%,
      rgba(0, 0, 0, 0.5) 50%,
      transparent 58%,
      transparent 100%
    );
    -webkit-mask-size: 320% 320%;
    mask-size: 320% 320%;
    -webkit-mask-repeat: no-repeat;
    mask-repeat: no-repeat;
    -webkit-mask-position: 100% 100%;
    mask-position: 100% 100%;
    will-change: mask-position, -webkit-mask-position;
    animation: sweepMaskIn 0.55s cubic-bezier(0.25, 1, 0.5, 1) forwards;
  }

  @keyframes sweepMaskIn {
    0% {
      -webkit-mask-position: 100% 100%;
      mask-position: 100% 100%;
    }
    100% {
      -webkit-mask-position: 0% 0%;
      mask-position: 0% 0%;
    }
  }

  @media (prefers-reduced-motion: reduce) {
    .game-icon-layer.game-icon-prev.sweeping,
    .game-icon-layer.game-icon-curr.sweeping {
      animation: none;
      -webkit-mask-image: none;
      mask-image: none;
    }
  }
</style>
