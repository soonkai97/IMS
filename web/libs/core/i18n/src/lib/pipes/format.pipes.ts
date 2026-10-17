import { Pipe, PipeTransform, inject } from '@angular/core';
import {
  MAX_QTY_DECIMALS,
  MONEY_DECIMALS,
  UNIT_PRICE_DECIMALS,
  formatTimestampText
} from '@ims/utils';
import { I18nService } from '../services/i18n.service';

const EMPTY_VALUE = '-';

/** "09/10/2026 14:05" (24 hour clock) in the business time zone (Asia/Kuala_Lumpur, BR-10), the same in every language. */
@Pipe({ name: 'appDate' })
export class AppDatePipe implements PipeTransform {
  transform(value: string | undefined | null, withTime = true): string {
    return value ? formatTimestampText(value, withTime) : EMPTY_VALUE;
  }
}

/** Quantities: up to 4 decimals (the API limit), trailing zeros trimmed (docs/design/2026-refresh-plan.md D1). */
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

/**
 * A quantity with its sign written out: "+5" adds stock, "-5" removes it, "0" has no sign. The sign is the text a screen
 * reader speaks, so the meaning never rests on colour (docs/design/mvp-ux.md 3.9).
 */
@Pipe({ name: 'appSignedQty', pure: false })
export class AppSignedQtyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | undefined | null): string {
    if (value === undefined || value === null) {
      return EMPTY_VALUE;
    }
    return new Intl.NumberFormat(this.i18n.$locale(), {
      maximumFractionDigits: MAX_QTY_DECIMALS,
      signDisplay: 'exceptZero'
    }).format(value);
  }
}

/**
 * Amounts with exactly 2 decimals and a thousands separator, never abbreviated: 12400 -> 12,400.00 (D1). Rounds half
 * away from zero like the API. The currency is deployment specific, so no symbol: labels say "(RM)" (D2). A missing amount
 * renders nothing: a cost the user may not see leaves no "-" and no "0.00" behind.
 */
@Pipe({ name: 'appMoney', pure: false })
export class AppMoneyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | undefined | null): string {
    if (value === undefined || value === null) {
      return '';
    }
    return new Intl.NumberFormat(this.i18n.$locale(), {
      minimumFractionDigits: MONEY_DECIMALS,
      maximumFractionDigits: MONEY_DECIMALS,
      signDisplay: 'negative'
    }).format(value);
  }
}

/** Unit prices (cost per base unit): up to 4 decimals, trailing zeros trimmed to 2 ("10.00", "3.3333", "0.003"). Nothing renders for a missing price. */
@Pipe({ name: 'appUnitPrice', pure: false })
export class AppUnitPricePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: number | undefined | null): string {
    if (value === undefined || value === null) {
      return '';
    }
    return new Intl.NumberFormat(this.i18n.$locale(), {
      minimumFractionDigits: MONEY_DECIMALS,
      maximumFractionDigits: UNIT_PRICE_DECIMALS,
      signDisplay: 'negative'
    }).format(value);
  }
}
