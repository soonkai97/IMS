import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';

export const THEME_MODE = {
  System: 'system',
  Light: 'light',
  Dark: 'dark'
} as const;

export type ThemeMode = (typeof THEME_MODE)[keyof typeof THEME_MODE];

const THEME_STORAGE_KEY = 'ims.theme';
const THEME_ATTRIBUTE = 'data-theme';

export function isThemeMode(value: unknown): value is ThemeMode {
  return value === THEME_MODE.System || value === THEME_MODE.Light || value === THEME_MODE.Dark;
}

function readStoredMode(): ThemeMode {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return isThemeMode(stored) ? stored : THEME_MODE.System;
  } catch {
    return THEME_MODE.System;
  }
}

/**
 * Light / dark / follow-the-OS. "System" removes the data-theme attribute so the stylesheet's prefers-color-scheme
 * rule decides; the other two force a palette (libs/shared/styles globals/_themes.scss).
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);

  readonly $mode = signal<ThemeMode>(readStoredMode());

  private readonly applyMode = effect(() => {
    const mode = this.$mode();
    const root = this.document.documentElement;
    if (mode === THEME_MODE.System) {
      root.removeAttribute(THEME_ATTRIBUTE);
    } else {
      root.setAttribute(THEME_ATTRIBUTE, mode);
    }
  });

  setMode(mode: ThemeMode): void {
    this.$mode.set(mode);
    try {
      localStorage.setItem(THEME_STORAGE_KEY, mode);
    } catch {
      // Not persisted when storage is blocked.
    }
  }
}
