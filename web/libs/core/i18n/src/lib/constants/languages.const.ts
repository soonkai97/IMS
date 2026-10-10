export const LANGUAGE = {
  English: 'en',
  Chinese: 'zh',
  Malay: 'ms'
} as const;

export type LanguageCode = (typeof LANGUAGE)[keyof typeof LANGUAGE];

export interface LanguageOption {
  readonly code: LanguageCode;
  /** Name in its own language, so users can find theirs. */
  readonly label: string;
  /** BCP 47 tag for Intl formatting. */
  readonly locale: string;
}

export const LANGUAGES: readonly LanguageOption[] = [
  { code: LANGUAGE.English, label: 'English', locale: 'en-GB' },
  { code: LANGUAGE.Chinese, label: '中文', locale: 'zh-CN' },
  { code: LANGUAGE.Malay, label: 'Bahasa Melayu', locale: 'ms-MY' }
];

export const DEFAULT_LANGUAGE: LanguageCode = LANGUAGE.English;
export const LANGUAGE_STORAGE_KEY = 'ims.language';

export function isLanguageCode(value: unknown): value is LanguageCode {
  return LANGUAGES.some(language => language.code === value);
}
