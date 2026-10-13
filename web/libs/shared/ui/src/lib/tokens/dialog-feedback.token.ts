import { InjectionToken, Type } from '@angular/core';

/**
 * The component an open dialog renders above its content (the toast host, provided by the app): a dialog is modal, so a
 * failure raised while it is open must be shown inside it. shared/ui cannot import core/notification, hence a token.
 * The component takes an `inDialog` input.
 */
export const UI_DIALOG_FEEDBACK = new InjectionToken<Type<unknown> | undefined>(
  'UI_DIALOG_FEEDBACK',
  { factory: () => undefined }
);
