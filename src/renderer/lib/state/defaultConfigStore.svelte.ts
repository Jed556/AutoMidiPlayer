import type { DefaultConfigUpdates } from '../../../shared/types/games.js';
import { api } from '../backend/index.js';

class DefaultConfigStore {
  report = $state<DefaultConfigUpdates | null>(null);
  error = $state('');
  loading = $state(false);
  saving = $state(false);

  async load() {
    if (this.loading || this.saving) return;
    this.loading = true;
    this.error = '';
    try {
      this.report = await api.games.getDefaultConfigUpdates();
    } catch (error) {
      this.error = String(error);
    } finally {
      this.loading = false;
    }
  }

  async apply(replaceIds: ReadonlySet<string>): Promise<boolean> {
    if (!this.report || this.saving || this.loading) return false;
    this.saving = true;
    this.error = '';
    const choices = this.report.conflicts.map(conflict => ({
      id: conflict.id, token: conflict.token,
      action: replaceIds.has(conflict.id) ? 'replace' as const : 'keep' as const,
    }));
    const duplicateChoices = (this.report.duplicates ?? []).map(duplicate => ({
      id: duplicate.id, token: duplicate.token,
      action: replaceIds.has(duplicate.id) ? 'replace' as const : 'keep' as const,
    }));
    try {
      this.report = await api.games.resolveDefaultConfigUpdates(choices, duplicateChoices);
      return true;
    } catch (error) {
      this.error = String(error);
      // A file may have changed while the comparison was open, or a later write failed.
      // Refresh the preview before letting the user consent to another attempt.
      try { this.report = await api.games.getDefaultConfigUpdates(); }
      catch { this.report = null; }
      return false;
    } finally {
      this.saving = false;
    }
  }
}

export const defaultConfigStore = new DefaultConfigStore();
