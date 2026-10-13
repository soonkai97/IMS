import { InjectionToken, Type } from '@angular/core';

/** The component an open dialog renders above its content (the toast host; shared/ui cannot import it). It takes `inDialog`. */
export const UI_DIALOG_FEEDBACK = new InjectionToken<Type<unknown> | undefined>(
  'UI_DIALOG_FEEDBACK',
  { factory: () => undefined }
);
