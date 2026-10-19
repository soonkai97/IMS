import { BUSINESS_TIME_ZONE } from '../constants/format.const';
import { DATE_EXAMPLE, FALLBACK_DATE_LOCALE, MIN_FILTER_YEAR } from './date-text.const';

const MONTHS = 12;
const MONTH_NAME_PREFIX = 3;
// Two digits for month and day: `2026-10-1` is a half-typed `2026-10-15` and must not apply 1 October on the way.
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
// The form the tables use in Chinese (2026年10月15日); the final 日 is required for the same reason.
const CJK_DATE = /^(\d{4}) ?年 ?(\d{1,2}) ?月 ?(\d{1,2}) ?日$/u;
// Day first, as everywhere in Malaysia: 09/10/2026 is 9 October.
const NUMERIC_DATE = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{4})$/;
// Eight digits with no separator, day first: 17102026 is 17 October 2026 (a phone keypad has no slash on its first page).
const COMPACT_DATE = /^(\d{2})(\d{2})(\d{4})$/;
const NAMED_DATE = /^(\d{1,2}) (\S+) (\d{4})$/u;

const monthNameCache = new Map<string, readonly string[]>();

/** "Jan" ... "Dec" in the language (Intl, the same source as the date pipe, so the field and the tables agree). */
function monthNames(locale: string): readonly string[] {
  const cached = monthNameCache.get(locale);
  if (cached) {
    return cached;
  }
  const format = new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' });
  const names = Array.from({ length: MONTHS }, (_, month) =>
    format.format(new Date(Date.UTC(2026, month, 1)))
  );
  monthNameCache.set(locale, names);
  return names;
}

/** "Sept", "sep." and "September" all mean Sep; names that are not Latin ("10月") must match whole. */
function monthKey(name: string): string {
  const lower = name.toLowerCase().replace(/\.$/, '');
  return /^[a-z]/.test(lower) ? lower.slice(0, MONTH_NAME_PREFIX) : lower;
}

function monthFromName(name: string, locale: string): number | null {
  const key = monthKey(name);
  for (const candidate of [locale, FALLBACK_DATE_LOCALE]) {
    const month = monthNames(candidate).findIndex(known => monthKey(known) === key);
    if (month >= 0) {
      return month + 1;
    }
  }
  return null;
}

/** A day the calendar has (no 31 Feb, no month 13). */
function isRealDay(year: number, month: number, day: number): boolean {
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

/** yyyy-MM-dd for a real calendar day from the year 2000 on, else null. */
function toIso(year: number, month: number, day: number): string | null {
  if (!isRealDay(year, month, day) || year < MIN_FILTER_YEAR) {
    return null;
  }
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

/**
 * What the user typed, as yyyy-MM-dd, or null when it is no date. Accepts `09/10/2026` (day first, the app format),
 * `09102026`, `09 Oct 2026` (month name in the active language or English), `2026-10-09` and `2026年10月9日`.
 */
export function parseDateText(text: string, locale: string): string | null {
  const trimmed = text.trim().replace(/\s+/g, ' ');
  const iso = ISO_DATE.exec(trimmed);
  if (iso) {
    return toIso(Number(iso[1]), Number(iso[2]), Number(iso[3]));
  }
  const numeric = NUMERIC_DATE.exec(trimmed);
  if (numeric) {
    return toIso(Number(numeric[3]), Number(numeric[2]), Number(numeric[1]));
  }
  const compact = COMPACT_DATE.exec(trimmed);
  if (compact) {
    return toIso(Number(compact[3]), Number(compact[2]), Number(compact[1]));
  }
  const cjk = CJK_DATE.exec(trimmed);
  if (cjk) {
    return toIso(Number(cjk[1]), Number(cjk[2]), Number(cjk[3]));
  }
  const named = NAMED_DATE.exec(trimmed);
  const month = named ? monthFromName(named[2], locale) : null;
  return named && month ? toIso(Number(named[3]), month, Number(named[1])) : null;
}

/** `09/10/2026` for a yyyy-MM-dd value, in every language (owner decision); an empty value or a day the calendar does not have gives an empty text. */
export function formatDateText(iso: string): string {
  const match = ISO_DATE.exec(iso);
  if (!match || !isRealDay(Number(match[1]), Number(match[2]), Number(match[3]))) {
    return '';
  }
  return `${match[3]}/${match[2]}/${match[1]}`;
}

/** The example the error sentence shows. */
export function exampleDateText(): string {
  return formatDateText(DATE_EXAMPLE);
}

/**
 * `09/10/2026 14:05` (24 hour clock) for a UTC timestamp, or `09/10/2026` without the time, in the business time zone
 * (BR-10) and in every language. Built from the parts so no locale can reorder or re-style it.
 */
export function formatTimestampText(value: string, withTime: boolean): string {
  const parts = new Intl.DateTimeFormat('en-GB', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZone: BUSINESS_TIME_ZONE
  }).formatToParts(new Date(value));
  const part = (type: Intl.DateTimeFormatPartTypes): string =>
    parts.find(candidate => candidate.type === type)?.value ?? '';
  const day = `${part('day')}/${part('month')}/${part('year')}`;
  return withTime ? `${day} ${part('hour')}:${part('minute')}` : day;
}

/** Both dates are yyyy-MM-dd (or empty), so comparing the strings compares the days. */
export function isDateRangeInvalid(from: string, to: string): boolean {
  return from !== '' && to !== '' && from > to;
}
