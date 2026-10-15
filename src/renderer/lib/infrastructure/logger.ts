const isTauriEnv =
  typeof window !== 'undefined' &&
  ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);

class Logger {
  private localBuffer: string[] = [];

  /**
   * Logs a general info message to app.log and browser console
   */
  async info(message: string, caller?: string, file?: string, line?: number) {
    console.log(`[INFO] ${caller ? `[${caller}] ` : ''}${message}`);
    this.appendLocal(`[INFO] ${caller ? `[${caller}] ` : ''}${message}`);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('log_message', {
          level: 'info',
          message,
          channel: 'app',
          caller: caller ?? 'renderer',
          file,
          line,
        });
      } catch (err) {
        console.warn('[LoggerService] Failed to invoke log_message:', err);
      }
    }
  }

  /**
   * Logs a warning to app.log and browser console
   */
  async warn(message: string, caller?: string, file?: string, line?: number) {
    console.warn(`[WARN] ${caller ? `[${caller}] ` : ''}${message}`);
    this.appendLocal(`[WARN] ${caller ? `[${caller}] ` : ''}${message}`);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('log_message', {
          level: 'warn',
          message,
          channel: 'app',
          caller: caller ?? 'renderer',
          file,
          line,
        });
      } catch (err) {
        console.warn('[LoggerService] Failed to invoke log_message:', err);
      }
    }
  }

  /**
   * Logs an explicit error to errors.log, app.log, and browser console
   */
  async error(
    message: string,
    errorOrStack?: unknown,
    caller?: string,
    file?: string,
    line?: number,
  ) {
    let stackStr: string | undefined;
    if (errorOrStack instanceof Error) {
      stackStr = errorOrStack.stack;
    } else if (typeof errorOrStack === 'string') {
      stackStr = errorOrStack;
    }

    const fullMsg = stackStr ? `${message}\n${stackStr}` : message;
    console.error(`[ERROR] ${caller ? `[${caller}] ` : ''}${fullMsg}`);
    this.appendLocal(`[ERROR] ${caller ? `[${caller}] ` : ''}${fullMsg}`);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('log_message', {
          level: 'error',
          message: fullMsg,
          channel: 'errors',
          caller: caller ?? 'renderer',
          file,
          line,
        });
      } catch (err) {
        console.warn('[LoggerService] Failed to invoke log_message:', err);
      }
    }
  }

  /**
   * Logs a diagnostic step routed automatically to the matching channel file
   * (e.g. MIDI_*, PLAYBACK_*, MAPPING_*, etc.)
   */
  async step(stepName: string, details?: string) {
    const logStr = details ? `STEP: ${stepName} | ${details}` : `STEP: ${stepName}`;
    console.debug(`[STEP] ${logStr}`);
    this.appendLocal(logStr);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('log_step', { step: stepName, details });
      } catch (err) {
        console.warn('[LoggerService] Failed to invoke log_step:', err);
      }
    }
  }

  /**
   * Logs UI navigation / view visits
   */
  async pageVisit(pageName: string, source?: string) {
    const detailStr = source ? `page=${pageName} | source=${source}` : `page=${pageName}`;
    console.debug(`[PAGE_VISIT] ${detailStr}`);
    this.appendLocal(`PAGE_VISIT: ${detailStr}`);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('log_page_visit', { pageName, source });
      } catch (err) {
        console.warn('[LoggerService] Failed to invoke log_page_visit:', err);
      }
    }
  }

  /**
   * Clears all channel log files on disk
   */
  async clearLogs(): Promise<boolean> {
    this.localBuffer = [];
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('clear_app_logs');
        return true;
      } catch (err) {
        console.warn('[LoggerService] Failed to clear app logs:', err);
        return false;
      }
    }
    return true;
  }

  /**
   * Opens the %LocalAppData%\AutoMidiPlayer\logs directory in Windows Explorer
   */
  async openLogsFolder(): Promise<boolean> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('open_logs_folder');
        return true;
      } catch (err) {
        console.warn('[LoggerService] Failed to open logs folder:', err);
        return false;
      }
    }
    return true;
  }

  /**
   * Reads the tail of a specified channel log file (default 100 lines)
   */
  async readTail(channel: string = 'app', maxLines: number = 100): Promise<string> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        return await invoke<string>('read_log_tail', { channel, maxLines });
      } catch (err) {
        console.warn('[LoggerService] Failed to read log tail:', err);
        return '';
      }
    }
    return this.localBuffer.slice(-maxLines).join('\n');
  }

  /**
   * Updates crash log verbosity level (0 = ErrorsOnly, 1 = WarningsAndErrors, 2 = AllSteps)
   */
  async setVerbosity(verbosity: number): Promise<boolean> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('set_crash_log_verbosity', { verbosity });
        return true;
      } catch (err) {
        console.warn('[LoggerService] Failed to set crash log verbosity:', err);
        return false;
      }
    }
    return true;
  }

  /**
   * Updates whether debug mode is enabled
   */
  async setDebugMode(enabled: boolean): Promise<boolean> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('set_debug_mode', { enabled });
        return true;
      } catch (err) {
        console.warn('[LoggerService] Failed to set debug mode:', err);
        return false;
      }
    }
    return true;
  }

  private appendLocal(msg: string) {
    const ts = new Date().toISOString().substring(11, 23);
    this.localBuffer.push(`[${ts}] ${msg}`);
    if (this.localBuffer.length > 500) {
      this.localBuffer.shift();
    }
  }
}

export const logger = new Logger();
