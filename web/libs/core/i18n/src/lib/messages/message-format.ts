export type MessageParams = Readonly<Record<string, string | number>>;

const PLURAL_KEYWORD = 'plural';

interface Branch {
  readonly selector: string;
  readonly text: string;
}

/** Index of the brace that closes the one opened at `start`, or -1. */
function findClosing(text: string, start: number): number {
  let depth = 0;
  for (let index = start; index < text.length; index += 1) {
    if (text[index] === '{') {
      depth += 1;
    }
    if (text[index] === '}') {
      depth -= 1;
    }
    if (depth === 0) {
      return index;
    }
  }
  return -1;
}

/** "one {1 line} other {# lines}" -> [{one, "1 line"}, {other, "# lines"}] */
function parseBranches(source: string): Branch[] {
  const branches: Branch[] = [];
  let cursor = 0;
  while (cursor < source.length) {
    const open = source.indexOf('{', cursor);
    if (open < 0) {
      break;
    }
    const close = findClosing(source, open);
    if (close < 0) {
      break;
    }
    branches.push({
      selector: source.slice(cursor, open).trim(),
      text: source.slice(open + 1, close)
    });
    cursor = close + 1;
  }
  return branches;
}

function selectBranch(branches: readonly Branch[], count: number, locale: string): string {
  const exact = branches.find(branch => branch.selector === `=${count}`);
  if (exact) {
    return exact.text;
  }
  const category = new Intl.PluralRules(locale).select(count);
  const matched =
    branches.find(branch => branch.selector === category) ??
    branches.find(branch => branch.selector === 'other');
  return matched?.text ?? '';
}

function formatPlaceholder(body: string, params: MessageParams, locale: string): string {
  const parts = body.split(',');
  const name = parts[0].trim();
  const value = params[name];
  if (parts.length < 3 || parts[1].trim() !== PLURAL_KEYWORD) {
    return value === undefined ? `{${name}}` : String(value);
  }
  const count = Number(value ?? 0);
  const branchSource = parts.slice(2).join(',');
  const chosen = selectBranch(parseBranches(branchSource), count, locale);
  return formatMessage(chosen.replace(/#/g, String(count)), params, locale);
}

/**
 * ICU-lite formatter: "{name}" placeholders and "{count, plural, one {...} other {# ...}}". Copy is never built by
 * concatenation (docs/design/mvp-ux.md D9), so translators control word order.
 */
export function formatMessage(template: string, params: MessageParams = {}, locale = 'en'): string {
  let result = '';
  let cursor = 0;
  while (cursor < template.length) {
    const open = template.indexOf('{', cursor);
    if (open < 0) {
      result += template.slice(cursor);
      break;
    }
    const close = findClosing(template, open);
    if (close < 0) {
      result += template.slice(cursor);
      break;
    }
    result +=
      template.slice(cursor, open) +
      formatPlaceholder(template.slice(open + 1, close), params, locale);
    cursor = close + 1;
  }
  return result;
}
