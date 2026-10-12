/**
 * Long values for containment tests (L00): the same text in the shapes that break layouts differently. `length` counts
 * UTF-16 units, as the API does, and a surrogate pair or joined emoji is never cut in half.
 */
export const LONG_VARIANTS = ['latin', 'spaces', 'chinese', 'emoji'] as const;
export type LongVariant = (typeof LONG_VARIANTS)[number];

const UNITS: Record<LongVariant, string[]> = {
  latin: ['W'],
  spaces: ['lorem ', 'ipsum ', 'dolor ', 'sit ', 'amet '],
  chinese: ['库存管理系统'],
  emoji: ['📦', '👩‍👩‍👧‍👦', '🚚']
};

/** `marker` first (so the value stays unique and findable), then the variant repeated up to `length` units. */
export function longValue(variant: LongVariant, length: number, marker = ''): string {
  let text = marker;
  const units = UNITS[variant];
  for (let index = 0; ; index++) {
    const next = units[index % units.length]!;
    if (text.length + next.length > length) {
      return text;
    }
    text += next;
  }
}
