import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { FormControl } from '@angular/forms';
import { TAX_FIELD } from '@ims/models';
import { TAX_SHAPE_ERROR, cleanTaxField, taxFieldShape } from './tax-field.validator';

const SERVER = resolve(__dirname, '../../../../../../../src/Ims.Domain');
const read = (path: string) => readFileSync(resolve(SERVER, path), 'utf8');
const errorOf = (field: keyof typeof TAX_FIELD, value: string | null) =>
  taxFieldShape(TAX_FIELD[field])(new FormControl(value));

describe('taxFieldShape (the rules of Ims.Domain TaxFieldShapes)', () => {
  it('lets a blank value through: the field is optional', () => {
    for (const value of [null, '', '   ']) {
      expect(errorOf('HsCode', value)).toBeNull();
    }
  });

  it('HS code: digits and dots, 16 characters', () => {
    expect(errorOf('HsCode', '0901.21')).toBeNull();
    expect(errorOf('HsCode', '1234567890123456')).toBeNull();
    expect(errorOf('HsCode', '12345678901234567')).toEqual({ [TAX_SHAPE_ERROR]: true });
    expect(errorOf('HsCode', '0901-21')).toEqual({ [TAX_SHAPE_ERROR]: true });
    expect(errorOf('HsCode', 'AB12')).toEqual({ [TAX_SHAPE_ERROR]: true });
  });

  it('SST code and SST registration number: letters, digits and a hyphen', () => {
    expect(errorOf('SstCode', 'ST-01')).toBeNull();
    expect(errorOf('SstCode', 'ST 01')).not.toBeNull();
    expect(errorOf('SstCode', 'A'.repeat(21))).not.toBeNull();
    expect(errorOf('SstRegNo', 'W10-1808-32000001')).toBeNull();
    expect(errorOf('SstRegNo', 'W'.repeat(31))).not.toBeNull();
    expect(errorOf('SstRegNo', 'W10_1808')).not.toBeNull();
  });

  it('TIN: letters and digits only (no hyphen), 20 characters', () => {
    expect(errorOf('Tin', 'C1234567890')).toBeNull();
    expect(errorOf('Tin', 'C-1234567890')).not.toBeNull();
    expect(errorOf('Tin', 'C'.repeat(21))).not.toBeNull();
  });

  it('is ASCII only: a Unicode digit or letter is refused, like the server', () => {
    expect(errorOf('HsCode', '٣٤٥')).not.toBeNull(); // Arabic-Indic digits
    expect(errorOf('Tin', 'ÀB12')).not.toBeNull();
    expect(errorOf('Tin', '甲乙')).not.toBeNull();
  });

  it('judges the trimmed value: spaces at the ends neither fail nor count toward the length', () => {
    expect(errorOf('HsCode', '  0901.21 ')).toBeNull();
    expect(errorOf('HsCode', ` ${'1'.repeat(16)} `)).toBeNull();
    expect(errorOf('HsCode', '09 01')).not.toBeNull();
  });

  it('cleanTaxField is what the server stores', () => {
    expect(cleanTaxField('  ab12 ')).toBe('ab12'); // case kept
    expect(cleanTaxField('   ')).toBe('');
    expect(cleanTaxField(null)).toBe('');
    expect(cleanTaxField(undefined)).toBe('');
  });
});

describe('TAX_FIELD matches the server (read from the C# source)', () => {
  const limits = read('Common/Constants/Limits.cs');
  const shapes = read('Modules/Services/TaxFieldShapes.cs');

  it.each([
    ['HsCode', 'HsCodeMaxLength'],
    ['SstCode', 'SstCodeMaxLength'],
    ['Tin', 'TinMaxLength'],
    ['SstRegNo', 'SstRegNoMaxLength']
  ] as const)('%s is %s long at most', (field, constant) => {
    expect(limits).toContain(`public const int ${constant} = ${TAX_FIELD[field].maxLength};`);
  });

  it.each([
    ['HsCode', 'HsCodeError', 'digits: true, letters: false, dot: true, hyphen: false'],
    ['SstCode', 'SstCodeError', 'digits: true, letters: true, dot: false, hyphen: true'],
    ['Tin', 'TinError', 'digits: true, letters: true, dot: false, hyphen: false'],
    ['SstRegNo', 'SstRegNoError', 'digits: true, letters: true, dot: false, hyphen: true']
  ] as const)('%s allows the characters %s allows', (field, method, flags) => {
    const line = shapes.split('\n').find(row => row.includes(`string? ${method}(`)) ?? '';
    expect(line).toContain(flags);
    expect(line).toContain(`Limits.${field}MaxLength`);
  });

  it('the server trims only the ends, and a blank value is none', () => {
    expect(shapes).toContain('string.IsNullOrWhiteSpace(value) ? null : value.Trim()');
  });
});
