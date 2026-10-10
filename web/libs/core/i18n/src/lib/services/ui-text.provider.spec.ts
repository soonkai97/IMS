import { TestBed } from '@angular/core/testing';
import { UI_TEXT } from '@ims/ui';
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

  it('keeps the other validation sentences', () => {
    expect(text().validationMessage('required', true)).toBe('This field is required.');
    expect(text().validationMessage('maxlength', { requiredLength: 100 })).toBe(
      'Use at most 100 characters.'
    );
  });
});
