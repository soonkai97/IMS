import { TestBed } from '@angular/core/testing';
import { UI_TEXT } from '@ims/ui';
import { I18nService } from './i18n.service';
import { provideUiText } from './ui-text.provider';

describe('UI text for the kit', () => {
  beforeEach(() => TestBed.configureTestingModule({ providers: [provideUiText()] }));
  const text = () => TestBed.inject(UI_TEXT)();

  it('PS-24: a value that is too large or too small says the limit', () => {
    expect(text().validationMessage('max', { max: 720, actual: 800 })).toBe('Enter 720 or less.');
    expect(text().validationMessage('min', { min: 0.001, actual: -5 })).toBe(
      'Enter 0.001 or more.'
    );
  });

  it('EA-06: a reason box says what a reason must be', () => {
    expect(text().reasonTooShort(5)).toBe('Enter a reason of at least 5 characters.');
  });

  it('D4: the required word and the legend come from the catalog', () => {
    expect(text().required).toBe('required');
    expect(text().requiredLegend).toBe('* means required');
  });

  it('R04: too many decimals says the limit: money has its own sentence, a quantity says 4', () => {
    expect(text().validationMessage('decimals', { max: 2 })).toBe(
      'Enter an amount with at most 2 decimals.'
    );
    expect(text().validationMessage('decimals', { max: 4 })).toBe('Use at most 4 decimals.');
  });

  it('R04: text a number field cannot read says what to type', () => {
    expect(text().validationMessage('badInput', true)).toBe(
      'Enter a number such as 1234.50, without commas or spaces.'
    );
  });

  it('R23: text that is no date names a date in the app format; the calendar button has a name', () => {
    expect(text().validationMessage('date', true)).toBe(
      'Enter a date as dd/mm/yyyy, such as 09/10/2026.'
    );
    expect(text().chooseDate).toBe('Choose date');
    expect(text().datePlaceholder).toBe('dd/mm/yyyy');
    expect(text().locale).toBe('en-GB');
  });

  it.each([
    ['en', 'Page 1 of 17, 1-25 of 415'],
    ['zh', '第 1 页，共 17 页，第 1-25 条，共 415 条'],
    ['ms', 'Halaman 1 daripada 17, 1-25 daripada 415']
  ] as const)(
    'TB: in %s the page and the range are one sentence joined as the language joins',
    async (language, sentence) => {
      await TestBed.inject(I18nService).setLanguage(language);

      expect(
        text().pageAnnouncement(text().pageStatus(1, 17), text().rangeStatus(1, 25, 415))
      ).toBe(sentence);
    }
  );

  it.each([
    ['zh', '日/月/年', '请输入日期（日/月/年），例如 09/10/2026。'],
    ['ms', 'hh/bb/tttt', 'Masukkan tarikh sebagai hh/bb/tttt, contohnya 09/10/2026.']
  ] as const)(
    'R23: in %s the date placeholder is %s and an invalid date names the pattern with the same example',
    async (language, placeholder, sentence) => {
      await TestBed.inject(I18nService).setLanguage(language);

      expect(text().datePlaceholder).toBe(placeholder);
      expect(text().validationMessage('date', true)).toBe(sentence);
    }
  );

  it('keeps the other validation sentences', () => {
    expect(text().validationMessage('required', true)).toBe('This field is required.');
    expect(text().validationMessage('maxlength', { requiredLength: 100 })).toBe(
      'Use at most 100 characters.'
    );
  });
});
