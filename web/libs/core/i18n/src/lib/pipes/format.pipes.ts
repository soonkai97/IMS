import { Pipe, PipeTransform, inject } from '@angular/core';
import { BUSINESS_TIME_ZONE, MAX_QTY_DECIMALS, MONEY_DECIMALS } from '@ims/utils';
import { I18nService } from '../services/i18n.service';

const EMPTY_VALUE = '-';

/** "09 Oct 2026 14:05" in the business time zone (Asia/Kuala_Lumpur, BR-10). */
@Pipe({ name: 'appDate', pure: false })
export class AppDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: string | undefined | null, withTime = true): string {
    if (!value) {
      return EMPTY_VALUE;
    }
    const options: Intl.DateTimeFormatOptions = {
      day: '2-digit',
      month: 'short',
      year: 'numeric',
      timeZone: BUSINESS_TIME_ZONE,
      ...(withTime ? { hour: '2-digit', minute: '2-digit', hour12: false } : {})
    };
    return new Intl.DateTimeFormat(this.i18n.$locale(), options).format(new Date(value));
  }
}

/** Quantities: up to 3 decimals, trailing zeros trimmed (docs/design/mvp-ux.md D10). */
@Pipe({ name: 'appQty', pure: false })
export class AppQtyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | undefined | null): string {
    if (value === undefined || value === null) {
      return EMPTY_VALUE;
    }
    return new Intl.NumberFormat(this.i18n.$locale(), {
      maximumFractionDigits: MAX_QTY_DECIMALS
    }).format(value);
  }
}

/** Amounts with two decimals (the currency is deployment specific, so no symbol). */
@Pipe({ name: 'appMoney', pure: false })
export class AppMoneyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | undefined | null): string {
    if (value === undefined || value === null) {
      return EMPTY_VALUE;
    }
    return new Intl.NumberFormat(this.i18n.$locale(), {
      minimumFractionDigits: MONEY_DECIMALS,
      maximumFractionDigits: MONEY_DECIMALS
    }).format(value);
  }
}
