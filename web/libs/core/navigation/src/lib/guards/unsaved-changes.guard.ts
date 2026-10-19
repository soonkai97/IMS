import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { defaultIfEmpty, map, of } from 'rxjs';
import { I18nService } from '@ims/core/i18n';
import { AuthService } from '@ims/core/identity';
import { ConfirmService } from '@ims/core/notification';

/** Implemented by pages with an editable form so leaving them can ask first. */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * "Discard changes?" before leaving a page with unsaved edits (docs/design/mvp-ux.md 6.4). Not when the session has expired: the
 * page cannot save any more, "Keep editing" would only repeat the failure, and the sign-in page says what happened (QA round,
 * defect 1). A screen that can keep its edits has done so already (SessionDraftService).
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = component => {
  if (!component.hasUnsavedChanges() || inject(AuthService).$sessionExpired()) {
    return true;
  }
  const i18n = inject(I18nService);
  return inject(ConfirmService)
    .ask({
      heading: i18n.t('dialog.discard.title'),
      body: i18n.t('dialog.discard.body'),
      confirmLabel: i18n.t('dialog.discard.confirm'),
      cancelLabel: i18n.t('dialog.discard.keep'),
      destructive: true
    })
    .pipe(
      map(() => true),
      defaultIfEmpty(false)
    );
};

export const ALLOW_LEAVING = of(true);
