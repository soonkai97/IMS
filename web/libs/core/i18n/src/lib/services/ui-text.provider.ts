import { EnvironmentProviders, inject, makeEnvironmentProviders } from '@angular/core';
import { UI_TEXT, UiText } from '@ims/ui';
import { MONEY_DECIMALS } from '@ims/utils';
import { I18nService } from './i18n.service';

/** Feeds the UI kit (shared/ui) its words from the active language. */
export function provideUiText(): EnvironmentProviders {
  return makeEnvironmentProviders([
    {
      provide: UI_TEXT,
      useFactory: () => {
        const i18n = inject(I18nService);
        return (): UiText => ({
          close: i18n.t('ui.close'),
          loading: i18n.t('ui.loading'),
          required: i18n.t('ui.required'),
          requiredLegend: i18n.t('form.requiredLegend'),
          showPassword: i18n.t('ui.showPassword'),
          hidePassword: i18n.t('ui.hidePassword'),
          previousPage: i18n.t('ui.previousPage'),
          nextPage: i18n.t('ui.nextPage'),
          clear: i18n.t('ui.clear'),
          retry: i18n.t('ui.retry'),
          saved: i18n.t('ui.saved'),
          chooseFile: i18n.t('ui.chooseFile'),
          dropFile: i18n.t('ui.dropFile'),
          selectPlaceholder: i18n.t('ui.selectPlaceholder'),
          dismiss: i18n.t('ui.dismiss'),
          showMore: i18n.t('ui.showMore'),
          showLess: i18n.t('ui.showLess'),
          errorTitle: i18n.t('ui.errorTitle'),
          reasonTooShort: length => i18n.t('validation.reasonMinLength', { length }),
          pageStatus: (page, pages) => i18n.t('ui.pageStatus', { page, pages }),
          rangeStatus: (from, to, total) => i18n.t('ui.rangeStatus', { from, to, total }),
          validationMessage: (key, error) => validationMessage(i18n, key, error)
        });
      }
    }
  ]);
}

function lengthOf(error: unknown): number {
  if (typeof error === 'object' && error !== null && 'requiredLength' in error) {
    return Number(error.requiredLength);
  }
  return 0;
}

function boundOf(error: unknown, member: 'min' | 'max'): number {
  if (typeof error === 'object' && error !== null && member in error) {
    return Number((error as Record<string, unknown>)[member]);
  }
  return 0;
}

function validationMessage(i18n: I18nService, key: string, error: unknown): string {
  switch (key) {
    case 'required':
      return i18n.t('validation.required');
    case 'minlength':
      return i18n.t('validation.minLength', { length: lengthOf(error) });
    case 'maxlength':
      return i18n.t('validation.maxLength', { length: lengthOf(error) });
    case 'min':
      return i18n.t('validation.min', { min: boundOf(error, 'min') });
    case 'max':
      return i18n.t('validation.max', { max: boundOf(error, 'max') });
    case 'decimals': {
      // Money has its own sentence (D1); any other limit, a quantity's 4, names the limit.
      const max = boundOf(error, 'max');
      return max === MONEY_DECIMALS
        ? i18n.t('validation.maxDecimals', { max })
        : i18n.t('validation.decimals', { max });
    }
    case 'badInput':
      return i18n.t('validation.badInput');
    case 'email':
      return i18n.t('validation.email');
    case 'server':
      return typeof error === 'string' ? error : i18n.t('validation.invalid');
    default:
      return i18n.t('validation.invalid');
  }
}
