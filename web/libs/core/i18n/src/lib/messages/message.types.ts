import { EN_MESSAGES } from './en.messages';

/** Every key of the English catalog. Typos in templates and services fail at compile time. */
export type MessageKey = keyof typeof EN_MESSAGES;

/** A complete catalog: zh and ms must define every key (the compiler enforces it). */
export type Messages = Readonly<Record<MessageKey, string>>;

/** For text that arrives as a plain string at run time (a route's `title`): is it a catalog key? */
export function isMessageKey(value: unknown): value is MessageKey {
  return typeof value === 'string' && Object.hasOwn(EN_MESSAGES, value);
}
