import { logger } from './logger.js';

const isTauriEnv =
  typeof window !== 'undefined' &&
  ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);

class Telemetry {
  private isInitialized = false;

  /**
   * Initializes telemetry state and global uncaught error listeners
   */
  init(telemetryOptIn?: boolean) {
    if (this.isInitialized) return;
    this.isInitialized = true;

    this.setupGlobalErrorHandlers();
    // Native startup loads the saved preference. Installing listeners must not
    // temporarily opt users in or out before settings finish loading.
    if (telemetryOptIn !== undefined) {
      this.setEnabled(telemetryOptIn).catch((err) =>
        console.warn('[TelemetryService] Failed to set initial telemetry state:', err),
      );
    }
  }

  /**
   * Enables or disables Sentry telemetry tracking
   */
  async setEnabled(enabled: boolean): Promise<boolean> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('set_telemetry_enabled', { enabled });
        return true;
      } catch (err) {
        console.warn('[TelemetryService] Failed to set telemetry enabled:', err);
        return false;
      }
    }
    return true;
  }

  /**
   * Dispatches a test event to Sentry with 10-minute cooldown validation
   */
  async sendTest(): Promise<{ success: boolean; message: string }> {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        const res = await invoke<string>('send_sentry_test');
        return { success: true, message: res };
      } catch (err: any) {
        const message = typeof err === 'string' ? err : err?.message || 'Failed to send Sentry test';
        return { success: false, message };
      }
    }
    return {
      success: false,
      message: 'Telemetry requires the desktop application.',
    };
  }

  /**
   * Captures an exception, logs it locally, and transmits to Sentry if telemetry is enabled
   */
  async captureException(error: unknown) {
    let errorType = 'Error';
    let message = 'Unknown error';
    let stack: string | undefined;

    if (error instanceof Error) {
      errorType = error.name || 'Error';
      message = error.message;
      stack = error.stack;
    } else if (typeof error === 'string') {
      message = error;
    } else {
      message = String(error);
    }

    await logger.error(`[UNHANDLED] ${errorType}: ${message}`, stack);

    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('capture_telemetry_exception', {
          errorType,
          message,
          stackTrace: stack,
        });
      } catch (err) {
        console.warn('[TelemetryService] Failed to capture telemetry exception:', err);
      }
    }
  }

  /**
   * Captures an arbitrary message into Sentry
   */
  async captureMessage(
    message: string,
    level: 'info' | 'warning' | 'error' | 'fatal' = 'info',
  ) {
    if (isTauriEnv) {
      try {
        const { invoke } = await import('@tauri-apps/api/core');
        await invoke('capture_telemetry_message', { message, level });
      } catch (err) {
        console.warn('[TelemetryService] Failed to capture telemetry message:', err);
      }
    }
  }

  /**
   * Sets up window error and promise rejection hooks
   */
  private setupGlobalErrorHandlers() {
    if (typeof window === 'undefined') return;

    window.addEventListener('error', (event) => {
      this.captureException(event.error || event.message).catch(() => {});
    });

    window.addEventListener('unhandledrejection', (event) => {
      this.captureException(event.reason || 'Unhandled Promise Rejection').catch(() => {});
    });
  }
}

export const telemetry = new Telemetry();
