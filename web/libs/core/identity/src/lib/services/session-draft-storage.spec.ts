import { DRAFT_MAX_CHARS, DRAFT_STORAGE_PREFIX, DRAFT_TTL_MS } from '../constants/identity.const';
import { clearDrafts, hasDrafts, saveDraft, takeDraft } from './session-draft-storage';

const USER = '00000000-0000-4000-8000-000000000007';
const OTHER = '00000000-0000-4000-8000-000000000008';
const KEY = `${DRAFT_STORAGE_PREFIX}${USER}.k`;

describe('session draft storage (QA defect 1, review M2 and M3)', () => {
  let now: jest.SpyInstance<number, []>;

  beforeEach(() => {
    sessionStorage.clear();
    now = jest.spyOn(Date, 'now').mockReturnValue(1_000_000);
  });
  afterEach(() => jest.restoreAllMocks());

  it('gives a draft back once, for the user who kept it', () => {
    expect(saveDraft(USER, 'k', { remark: 'x' })).toBe(true);

    expect(takeDraft(OTHER, 'k')).toBeUndefined();
    expect(takeDraft(USER, 'k')).toEqual({ remark: 'x' });
    expect(takeDraft(USER, 'k')).toBeUndefined();
  });

  it('stores the time it was saved', () => {
    saveDraft(USER, 'k', 1);

    expect(JSON.parse(sessionStorage.getItem(KEY) ?? '{}')).toEqual({
      savedAt: 1_000_000,
      value: 1
    });
  });

  it('still gives the draft back at the end of its lifetime and not one millisecond later', () => {
    saveDraft(USER, 'k', 'a');
    saveDraft(USER, 'j', 'b');

    now.mockReturnValue(1_000_000 + DRAFT_TTL_MS);
    expect(hasDrafts(USER)).toBe(true);
    expect(takeDraft(USER, 'k')).toBe('a');

    now.mockReturnValue(1_000_000 + DRAFT_TTL_MS + 1);
    expect(hasDrafts(USER)).toBe(false);
    expect(takeDraft(USER, 'j')).toBeUndefined();
    expect(sessionStorage.length).toBe(0);
  });

  it('does not keep a draft larger than the cap and does not read one that grew beyond it', () => {
    expect(saveDraft(USER, 'k', 'x'.repeat(DRAFT_MAX_CHARS))).toBe(false);
    expect(sessionStorage.length).toBe(0);

    sessionStorage.setItem(
      KEY,
      JSON.stringify({ savedAt: 1_000_000, value: 'x'.repeat(DRAFT_MAX_CHARS) })
    );
    expect(hasDrafts(USER)).toBe(false);
    expect(takeDraft(USER, 'k')).toBeUndefined();
  });

  it.each([
    ['text that is not JSON', 'not json'],
    ['a value with no time', JSON.stringify({ value: 1 })],
    ['a time that is not a number', JSON.stringify({ savedAt: 'now', value: 1 })],
    ['a time in the future', JSON.stringify({ savedAt: 9_000_000, value: 1 })],
    ['null', 'null'],
    ['a bare number', '5']
  ])('ignores %s written by someone else', (_name, stored) => {
    sessionStorage.setItem(KEY, stored);

    expect(hasDrafts(USER)).toBe(false);
    expect(takeDraft(USER, 'k')).toBeUndefined();
  });

  it('clears everyone but the user who stays', () => {
    saveDraft(USER, 'k', 1);
    saveDraft(OTHER, 'k', 2);

    clearDrafts(USER);

    expect(takeDraft(USER, 'k')).toBe(1);
    expect(takeDraft(OTHER, 'k')).toBeUndefined();
  });

  it('also removes entries that can no longer be read, of the user who stays too, and keeps the fresh one', () => {
    saveDraft(USER, 'fresh', 1);
    saveDraft(USER, 'old', 2);
    sessionStorage.setItem(`${DRAFT_STORAGE_PREFIX}${USER}.broken`, 'not json');
    sessionStorage.setItem(
      `${DRAFT_STORAGE_PREFIX}${USER}.huge`,
      JSON.stringify({ savedAt: 1_000_000, value: 'x'.repeat(DRAFT_MAX_CHARS) })
    );
    const oldKey = `${DRAFT_STORAGE_PREFIX}${USER}.old`;
    const stored = JSON.parse(sessionStorage.getItem(oldKey) ?? '{}');
    sessionStorage.setItem(
      oldKey,
      JSON.stringify({ ...stored, savedAt: 1_000_000 - DRAFT_TTL_MS - 1 })
    );

    clearDrafts(USER);

    expect(sessionStorage.length).toBe(1);
    expect(takeDraft(USER, 'fresh')).toBe(1);
  });

  it('survives storage that refuses writes', () => {
    jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    expect(saveDraft(USER, 'k', 1)).toBe(false);
  });
});
