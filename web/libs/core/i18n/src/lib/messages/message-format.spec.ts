import { EN_MESSAGES } from './en.messages';
import { formatMessage } from './message-format';
import { MS_MESSAGES } from './ms.messages';
import { ZH_MESSAGES } from './zh.messages';

describe('formatMessage', () => {
  it('replaces named placeholders and keeps unknown ones visible', () => {
    expect(formatMessage('Posted {docNo} by {name}', { docNo: 'RCV-1' })).toBe(
      'Posted RCV-1 by {name}'
    );
  });

  it('chooses the plural form and expands #', () => {
    const template = '{count, plural, one {1 line is short} other {# lines are short}}';

    expect(formatMessage(template, { count: 1 })).toBe('1 line is short');
    expect(formatMessage(template, { count: 3 })).toBe('3 lines are short');
  });

  it('supports text around a plural and an exact =0 branch', () => {
    const template = 'Stock: {n, plural, =0 {none} one {# unit} other {# units}}.';

    expect(formatMessage(template, { n: 0 })).toBe('Stock: none.');
    expect(formatMessage(template, { n: 2 })).toBe('Stock: 2 units.');
  });
});

describe('message catalogs', () => {
  const english = Object.keys(EN_MESSAGES).sort();

  it('zh and ms define exactly the English keys', () => {
    expect(Object.keys(ZH_MESSAGES).sort()).toEqual(english);
    expect(Object.keys(MS_MESSAGES).sort()).toEqual(english);
  });

  it('every translation uses the same placeholders as English', () => {
    const placeholders = (text: string): string[] =>
      [...text.matchAll(/\{(\w+)[,}]/g)].map(match => match[1]).sort();

    for (const key of english) {
      const expected = placeholders(EN_MESSAGES[key as keyof typeof EN_MESSAGES]);
      expect([key, placeholders(ZH_MESSAGES[key as keyof typeof ZH_MESSAGES])]).toEqual([
        key,
        expected
      ]);
      expect([key, placeholders(MS_MESSAGES[key as keyof typeof MS_MESSAGES])]).toEqual([
        key,
        expected
      ]);
    }
  });
});
