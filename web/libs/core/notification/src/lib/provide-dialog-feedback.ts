import { Provider } from '@angular/core';
import { UI_DIALOG_FEEDBACK } from '@ims/ui';
import { ToastHostComponent } from './components/toast-host.component';

/** Every dialog renders the toast host above its content while open (a modal makes the page's own host unreachable). */
export function provideDialogFeedback(): Provider {
  return { provide: UI_DIALOG_FEEDBACK, useValue: ToastHostComponent };
}
