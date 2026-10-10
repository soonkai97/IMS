import type { EN_MESSAGES } from './en.messages';

/** Every key of the English catalog. Typos in templates and services fail at compile time. */
export type MessageKey = keyof typeof EN_MESSAGES;

/** A complete catalog: zh and ms must define every key (the compiler enforces it). */
export type Messages = Readonly<Record<MessageKey, string>>;
