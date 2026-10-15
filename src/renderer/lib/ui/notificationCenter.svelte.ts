export interface ToastPositionOffset {
  bottom?: number | string;
  right?: number | string;
  top?: number | string;
  left?: number | string;
  x?: number | string;
  y?: number | string;
}

export interface ToastNotification {
  id: string;
  type: 'info' | 'success' | 'warning' | 'error';
  title: string;
  message?: string;
  durationMs?: number;
  action?: {
    label: string;
    onClick: () => void;
  };
}

class NotificationCenter {
  toasts = $state<ToastNotification[]>([]);
  toastOffset = $state<ToastPositionOffset>({});
  private offsetStack: { id: string; offset: ToastPositionOffset }[] = [];
  private timerMap = new Map<string, { timerId: any; startTime: number; remainingMs: number }>();
  private isPaused = false;

  get notifications(): ToastNotification[] {
    return this.toasts;
  }

  /**
   * Set custom position offset for toast notifications.
   * Can be called by any page or component to shift the toast container position.
   * Returns a cleanup function that resets or restores the previous offset.
   */
  setToastOffset(offset: ToastPositionOffset, id?: string): () => void {
    const entryId = id || `offset-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
    this.offsetStack = this.offsetStack.filter(item => item.id !== entryId);
    this.offsetStack.push({ id: entryId, offset });
    this.toastOffset = { ...offset };

    return () => {
      this.resetToastOffset(entryId);
    };
  }

  /**
   * Reset custom toast position offset.
   * If an id is provided, removes that entry from the stack and restores the previous offset.
   * If no id is provided, resets completely to default (no offset).
   */
  resetToastOffset(id?: string) {
    if (id) {
      this.offsetStack = this.offsetStack.filter(item => item.id !== id);
      this.toastOffset = this.offsetStack.length > 0
        ? { ...this.offsetStack[this.offsetStack.length - 1].offset }
        : {};
    } else {
      this.offsetStack = [];
      this.toastOffset = {};
    }
  }

  show(notification: Omit<ToastNotification, 'id'>) {
    const id = `toast-${Date.now()}-${Math.random().toString(36).substring(2, 6)}`;
    const durationMs = notification.durationMs ?? 4500;
    const toast: ToastNotification = {
      ...notification,
      id,
      durationMs
    };

    this.toasts = [...this.toasts, toast];

    if (durationMs > 0) {
      if (this.isPaused) {
        this.timerMap.set(id, { timerId: null, startTime: Date.now(), remainingMs: durationMs });
      } else {
        const timerId = setTimeout(() => {
          this.dismiss(id);
        }, durationMs);
        this.timerMap.set(id, { timerId, startTime: Date.now(), remainingMs: durationMs });
      }
    }
  }

  dismiss(id: string) {
    const entry = this.timerMap.get(id);
    if (entry?.timerId) {
      clearTimeout(entry.timerId);
    }
    this.timerMap.delete(id);
    this.toasts = this.toasts.filter(t => t.id !== id);
  }

  pauseAll() {
    this.isPaused = true;
    const now = Date.now();
    for (const [id, entry] of this.timerMap.entries()) {
      if (entry.timerId) {
        clearTimeout(entry.timerId);
        entry.timerId = null;
      }
      const elapsed = now - entry.startTime;
      entry.remainingMs = Math.max(0, entry.remainingMs - elapsed);
    }
  }

  resumeAll() {
    this.isPaused = false;
    const now = Date.now();
    for (const [id, entry] of this.timerMap.entries()) {
      entry.startTime = now;
      if (entry.remainingMs > 0) {
        entry.timerId = setTimeout(() => {
          this.dismiss(id);
        }, entry.remainingMs);
      } else {
        this.dismiss(id);
      }
    }
  }

  info(title: string, message?: string) {
    this.show({ type: 'info', title, message });
  }

  success(title: string, message?: string) {
    this.show({ type: 'success', title, message });
  }

  warning(title: string, message?: string) {
    this.show({ type: 'warning', title, message });
  }

  error(title: string, message?: string) {
    this.show({ type: 'error', title, message });
  }
}

export const notificationCenter = new NotificationCenter();
