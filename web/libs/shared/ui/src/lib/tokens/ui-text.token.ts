import { InjectionToken } from '@angular/core';

/**
 * Words the UI kit needs itself (close buttons, validation messages...). shared/ui cannot depend on core/i18n, so the
 * app provides a translated implementation through UI_TEXT; the English default keeps the kit usable on its own.
 */
export interface UiText {
  readonly close: string;
  readonly loading: string;
  readonly required: string;
  /** "* means required": the legend that explains the red asterisk once per form (D4). */
  readonly requiredLegend: string;
  readonly showPassword: string;
  readonly hidePassword: string;
  readonly previousPage: string;
  readonly nextPage: string;
  readonly clear: string;
  readonly retry: string;
  /** The word a button shows for 1.5s after a save that stays on the screen. */
  readonly saved: string;
  readonly chooseFile: string;
  readonly dropFile: string;
  readonly selectPlaceholder: string;
  readonly dismiss: string;
  /** The control under a clamped text that reveals the rest, and the one that folds it again. */
  readonly showMore: string;
  readonly showLess: string;
  readonly errorTitle: string;
  /** "Enter a reason of at least {length} characters." for a reason box that is empty or too short. */
  reasonTooShort(length: number): string;
  pageStatus(page: number, pages: number): string;
  rangeStatus(from: number, to: number, total: number): string;
  validationMessage(key: string, error: unknown): string;
}

const DEFAULT_UI_TEXT: UiText = {
  close: 'Close',
  loading: 'Loading',
  required: 'required',
  requiredLegend: '* means required',
  showPassword: 'Show password',
  hidePassword: 'Hide password',
  previousPage: 'Previous page',
  nextPage: 'Next page',
  clear: 'Clear',
  retry: 'Retry',
  saved: 'Saved',
  chooseFile: 'Choose file',
  dropFile: 'or drop a file here',
  selectPlaceholder: 'Select...',
  dismiss: 'Dismiss',
  showMore: 'Show more',
  showLess: 'Show less',
  errorTitle: 'Something went wrong',
  reasonTooShort: length => `Enter a reason of at least ${length} characters.`,
  pageStatus: (page, pages) => `Page ${page} of ${pages}`,
  rangeStatus: (from, to, total) => `${from}-${to} of ${total}`,
  validationMessage: (key, error) => (typeof error === 'string' ? error : `Invalid value (${key}).`)
};

/** A function so translated text stays reactive: call it inside computed()/templates. */
export const UI_TEXT = new InjectionToken<() => UiText>('UI_TEXT', {
  providedIn: 'root',
  factory: () => () => DEFAULT_UI_TEXT
});
