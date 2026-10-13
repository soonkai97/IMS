// Run with: node --test scripts/guard
import test from 'node:test';
import assert from 'node:assert/strict';
import { checkCommitMessage, MAX_SUBJECT } from './lib.mjs';

const merged = { fromTargetHistory: true };
/** A valid conventional subject of exactly `length` characters. */
const subjectOf = (length) => `fix: ${'a'.repeat(length - 5)}`;

test('a local commit of 72 characters passes, 73 fails', () => {
  assert.deepEqual(checkCommitMessage(subjectOf(MAX_SUBJECT)), []);
  assert.notDeepEqual(checkCommitMessage(subjectOf(MAX_SUBJECT + 1)), []);
});

test('a merged commit of 72 characters plus " (#80)" passes', () => {
  assert.deepEqual(checkCommitMessage(`${subjectOf(72)} (#80)`, merged), []);
});

test('a merged commit of 73 characters before the suffix fails', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(73)} (#80)`, merged), []);
});

test('the suffix may have four digits', () => {
  assert.deepEqual(checkCommitMessage(`${subjectOf(72)} (#1234)`, merged), []);
});

test('the same subject fails without the merged-commit option (local hook, PR title)', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(72)} (#80)`), []);
  assert.deepEqual(checkCommitMessage(subjectOf(72)), []);
});

test('a " (#80)" in the middle of the subject is not stripped', () => {
  assert.notDeepEqual(checkCommitMessage(`fix: ${'a'.repeat(40)} (#80) ${'b'.repeat(30)}`, merged), []);
});

test('two suffixes: only the trailing one is stripped, once', () => {
  assert.notDeepEqual(checkCommitMessage(`${subjectOf(72)} (#79) (#80)`, merged), []);
  assert.deepEqual(checkCommitMessage(`${subjectOf(60)} (#79) (#80)`, merged), []);
});

test('a subject that is only the suffix is still rejected', () => {
  assert.notDeepEqual(checkCommitMessage('(#80)', merged), []);
  assert.notDeepEqual(checkCommitMessage(' (#80)', merged), []);
});

test('other rules stay strict for merged commits', () => {
  assert.notDeepEqual(checkCommitMessage('Add a thing (#80)', merged), []);
  assert.notDeepEqual(checkCommitMessage('fix: thing. (#80)', merged), []);
});
