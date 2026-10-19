import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import {
  DEFAULT_LANGUAGE,
  LANGUAGES,
  LANGUAGE_STORAGE_KEY,
  LanguageCode,
  isLanguageCode
} from '../constants/languages.const';
import { DEFAULT_CURRENCY_LABEL } from '@ims/utils';
import { EN_MESSAGES } from '../messages/en.messages';
import { ROLE_LABELS } from '../messages/enum-labels';
import { MessageParams, formatMessage } from '../messages/message-format';
import { MessageKey, Messages } from '../messages/message.types';

async function loadMessages(language: LanguageCode): Promise<Messages> {
  switch (language) {
    case 'zh':
      return (await import('../messages/zh.messages')).ZH_MESSAGES;
    case 'ms':
      return (await import('../messages/ms.messages')).MS_MESSAGES;
    default:
      return EN_MESSAGES;
  }
}

function readStoredLanguage(): LanguageCode | undefined {
  try {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
    return isLanguageCode(stored) ? stored : undefined;
  } catch {
    return undefined;
  }
}

function browserLanguage(): LanguageCode {
  const preferred = navigator.language.slice(0, 2).toLowerCase();
  return isLanguageCode(preferred) ? preferred : DEFAULT_LANGUAGE;
}

/** Runtime language switching without rebuilds. English is bundled; zh and ms load on demand. */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly document = inject(DOCUMENT);

  private readonly $current = signal<LanguageCode>(DEFAULT_LANGUAGE);
  private readonly $messages = signal<Messages>(EN_MESSAGES);

  readonly $language = this.$current.asReadonly();
  readonly $locale = computed(
    () => LANGUAGES.find(language => language.code === this.$current())?.locale ?? 'en-GB'
  );

  /** Called once at startup (app initializer): stored choice, else the browser's language. */
  async init(): Promise<void> {
    await this.setLanguage(readStoredLanguage() ?? browserLanguage());
  }

  async setLanguage(language: LanguageCode): Promise<void> {
    const messages = await loadMessages(language);
    this.$messages.set(messages);
    this.$current.set(language);
    this.document.documentElement.lang = language;
    try {
      localStorage.setItem(LANGUAGE_STORAGE_KEY, language);
    } catch {
      // Storage can be blocked (private mode); the choice then lasts for this session only.
    }
  }

  /**
   * Display only, never a decision: the translated label of a standard role code, and for any other role the name the API gave.
   * A custom role is data and has no translation; a standard role shows in the person's language, like the header does.
   */
  roleLabel(code: string, name: string): string {
    const key = ROLE_LABELS[code];
    return key ? this.t(key) : name;
  }

  /** Label of an enum-like server value ("Posted", "WarehouseStaff"); unknown values are shown as they are. */
  enumLabel(labels: Readonly<Record<string, MessageKey>>, value: string | undefined): string {
    if (value === undefined) {
      return '';
    }
    const key = labels[value];
    return key ? this.t(key) : value;
  }

  /** Reactive: reading it inside computed()/templates re-evaluates when the language changes. */
  t(key: MessageKey, params?: MessageParams): string {
    const template = this.$messages()[key] ?? EN_MESSAGES[key];
    return formatMessage(template, { currency: DEFAULT_CURRENCY_LABEL, ...params }, this.$locale());
  }
}
