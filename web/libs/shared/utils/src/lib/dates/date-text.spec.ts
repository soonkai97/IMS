import { exampleDateText, formatDateText, isDateRangeInvalid, parseDateText } from './date-text';

const EN = 'en-GB';
const ZH = 'zh-CN';
const MS = 'ms-MY';

describe('formatDateText (R23)', () => {
  it('writes dd MMM yyyy with the month name of the language', () => {
    expect(formatDateText('2026-10-09', EN)).toBe('09 Oct 2026');
    expect(formatDateText('2026-10-09', MS)).toBe('09 Okt 2026');
    expect(formatDateText('2026-10-09', ZH)).toBe('09 10月 2026');
  });

  it('keeps the two digit day and the leap day', () => {
    expect(formatDateText('2028-02-29', EN)).toBe('29 Feb 2028');
    expect(formatDateText('2026-03-05', MS)).toBe('05 Mac 2026');
  });

  it('gives an empty text for an empty or unreadable value', () => {
    expect(formatDateText('', EN)).toBe('');
    expect(formatDateText('09 Oct 2026', EN)).toBe('');
  });

  it('gives an empty text, never "45 undefined 2026", for a day the calendar does not have', () => {
    for (const iso of ['2026-13-45', '2026-13-01', '2026-00-10', '2026-02-30', '2026-04-31']) {
      expect(formatDateText(iso, EN)).toBe('');
    }
  });

  it('shows the example the error sentence quotes', () => {
    expect(exampleDateText(EN)).toBe('09 Oct 2026');
  });
});

describe('parseDateText (R23)', () => {
  it('reads the app format and gives yyyy-MM-dd', () => {
    expect(parseDateText('09 Oct 2026', EN)).toBe('2026-10-09');
    expect(parseDateText('9 oct 2026', EN)).toBe('2026-10-09');
  });

  it('ignores spaces around and inside the text', () => {
    expect(parseDateText('  09   Oct  2026 ', EN)).toBe('2026-10-09');
  });

  it('reads the month names of every language, and English in any of them', () => {
    expect(parseDateText('09 Okt 2026', MS)).toBe('2026-10-09');
    expect(parseDateText('09 Oct 2026', MS)).toBe('2026-10-09');
    expect(parseDateText('09 10月 2026', ZH)).toBe('2026-10-09');
    expect(parseDateText('09 Oct 2026', ZH)).toBe('2026-10-09');
  });

  it('accepts Sept, Sep. and September for September', () => {
    for (const text of ['01 Sept 2026', '01 Sep. 2026', '01 September 2026']) {
      expect(parseDateText(text, EN)).toBe('2026-09-01');
    }
  });

  it('reads an ISO date', () => {
    expect(parseDateText('2026-10-09', EN)).toBe('2026-10-09');
  });

  it('does not read a half-typed ISO day: 2026-10-1 waits for 2026-10-15 instead of applying 1 October', () => {
    for (const half of ['2026-1-9', '2026-10-1', '2026-1-', '2026-10-']) {
      expect(parseDateText(half, EN)).toBeNull();
    }
    expect(parseDateText('2026-10-15', EN)).toBe('2026-10-15');
  });

  it('reads the form the tables use in Chinese, 2026年10月15日, in any language', () => {
    expect(parseDateText('2026年10月15日', ZH)).toBe('2026-10-15');
    expect(parseDateText('2026年1月5日', ZH)).toBe('2026-01-05');
    expect(parseDateText(' 2026 年 10 月 15 日 ', EN)).toBe('2026-10-15');
    expect(formatDateText('2026-10-15', ZH)).toBe('15 10月 2026');
  });

  it('does not read a half-typed Chinese day, or a Chinese day that does not exist', () => {
    for (const text of [
      '2026年10月1',
      '2026年10月15',
      '2026年10月',
      '2026年2月30日',
      '2026年13月1日',
      '1999年1月1日'
    ]) {
      expect(parseDateText(text, ZH)).toBeNull();
    }
  });

  it('reads dd/MM/yyyy with the day first, with / . or - between the parts', () => {
    expect(parseDateText('09/10/2026', EN)).toBe('2026-10-09');
    expect(parseDateText('9.10.2026', EN)).toBe('2026-10-09');
    expect(parseDateText('09-10-2026', EN)).toBe('2026-10-09');
    expect(parseDateText('12/01/2026', EN)).toBe('2026-01-12');
  });

  it('rejects a day the month does not have', () => {
    expect(parseDateText('31 Feb 2026', EN)).toBeNull();
    expect(parseDateText('2026-04-31', EN)).toBeNull();
    expect(parseDateText('31/04/2026', EN)).toBeNull();
    expect(parseDateText('0 Oct 2026', EN)).toBeNull();
  });

  it('knows the leap day', () => {
    expect(parseDateText('29 Feb 2028', EN)).toBe('2028-02-29');
    expect(parseDateText('29 Feb 2026', EN)).toBeNull();
    expect(parseDateText('29 Feb 2100', EN)).toBeNull();
  });

  it('rejects text that is no date', () => {
    for (const text of [
      '',
      ' ',
      'abc',
      '09 Oct',
      'Oct 09 2026',
      '09 Foo 2026',
      '9 10 2026',
      '13/13/2026'
    ]) {
      expect(parseDateText(text, EN)).toBeNull();
    }
  });

  it('reads a month by its first three letters and rejects a name that is no month', () => {
    expect(parseDateText('09 October 2026', EN)).toBe('2026-10-09');
    expect(parseDateText('09 Ox 2026', EN)).toBeNull();
  });

  it('rejects a year before 2000 (the API answers 500 for 0001-01-01)', () => {
    expect(parseDateText('01 Jan 1999', EN)).toBeNull();
    expect(parseDateText('0001-01-01', EN)).toBeNull();
    expect(parseDateText('01 Jan 2000', EN)).toBe('2000-01-01');
  });

  it('round-trips what it writes', () => {
    for (const locale of [EN, ZH, MS]) {
      for (let month = 1; month <= 12; month++) {
        const iso = `2026-${String(month).padStart(2, '0')}-15`;
        expect(parseDateText(formatDateText(iso, locale), locale)).toBe(iso);
      }
    }
  });
});

describe('isDateRangeInvalid (R23)', () => {
  it('is true only when both days are set and From is after To', () => {
    expect(isDateRangeInvalid('2026-10-10', '2026-10-09')).toBe(true);
    expect(isDateRangeInvalid('2026-10-09', '2026-10-09')).toBe(false);
    expect(isDateRangeInvalid('2026-10-08', '2026-10-09')).toBe(false);
  });

  it('is false while either side is empty', () => {
    expect(isDateRangeInvalid('', '2026-10-09')).toBe(false);
    expect(isDateRangeInvalid('2026-10-09', '')).toBe(false);
    expect(isDateRangeInvalid('', '')).toBe(false);
  });
});
