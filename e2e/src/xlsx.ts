import { inflateRawSync } from 'node:zlib';

/**
 * A reader for the first sheet of an .xlsx file, only as much as a test needs to look at what a download holds: the text of every
 * cell, row by row. An .xlsx is a zip; this walks its central directory and inflates the few entries it needs, so the suite takes no
 * spreadsheet library. Numbers come back as the text stored in the file (an amount of 50.00 is stored as "50").
 */
export type SheetRows = string[][];

const END_OF_CENTRAL_DIRECTORY = 0x06054b50;
const CENTRAL_ENTRY = 0x02014b50;
const LOCAL_ENTRY = 0x04034b50;

function unzip(buffer: Buffer): Map<string, Buffer> {
  let end = buffer.length - 22;
  while (end >= 0 && buffer.readUInt32LE(end) !== END_OF_CENTRAL_DIRECTORY) {
    end--;
  }
  if (end < 0) {
    throw new Error('Not a zip file (an .xlsx is one).');
  }
  const count = buffer.readUInt16LE(end + 10);
  let at = buffer.readUInt32LE(end + 16);
  const files = new Map<string, Buffer>();
  for (let index = 0; index < count; index++) {
    if (buffer.readUInt32LE(at) !== CENTRAL_ENTRY) {
      throw new Error('The zip directory is damaged.');
    }
    const method = buffer.readUInt16LE(at + 10);
    const compressed = buffer.readUInt32LE(at + 20);
    const nameLength = buffer.readUInt16LE(at + 28);
    const extraLength = buffer.readUInt16LE(at + 30);
    const commentLength = buffer.readUInt16LE(at + 32);
    const local = buffer.readUInt32LE(at + 42);
    const name = buffer.toString('utf8', at + 46, at + 46 + nameLength);
    if (buffer.readUInt32LE(local) !== LOCAL_ENTRY) {
      throw new Error('The zip entry is damaged.');
    }
    const dataAt = local + 30 + buffer.readUInt16LE(local + 26) + buffer.readUInt16LE(local + 28);
    const data = buffer.subarray(dataAt, dataAt + compressed);
    files.set(name, method === 0 ? data : inflateRawSync(data));
    at += 46 + nameLength + extraLength + commentLength;
  }
  return files;
}

const decode = (text: string): string =>
  text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#x([0-9a-f]+);/gi, (_match, hex: string) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_match, digits: string) => String.fromCodePoint(Number(digits)))
    .replace(/&amp;/g, '&');

/** Matches an element name with or without a namespace prefix (ClosedXML writes `<x:row>`, other writers `<row>`). */
const PREFIX = '(?:\\w+:)?';
const ANY = '[\\s\\S]*?';

const textRuns = new RegExp(`<${PREFIX}t(?:\\s[^>]*)?>(${ANY})</${PREFIX}t>`, 'g');
const stringItems = new RegExp(`<${PREFIX}si>(${ANY})</${PREFIX}si>`, 'g');
const sheetName = new RegExp(`<${PREFIX}sheet\\s[^>]*name="([^"]*)"`);
const rows = new RegExp(`<${PREFIX}row\\b[^>]*>(${ANY})</${PREFIX}row>`, 'g');
const cells = new RegExp(`<${PREFIX}c\\b([^>]*?)(?:/>|>(${ANY})</${PREFIX}c>)`, 'g');
const cellValue = new RegExp(`<${PREFIX}v>(${ANY})</${PREFIX}v>`);

/** The text of a string item: every run of it, in order. */
const textOf = (xml: string): string =>
  [...xml.matchAll(textRuns)].map(match => decode(match[1] ?? '')).join('');

/** Column letters to a zero-based index ("A" 0, "Q" 16, "AA" 26). */
const columnIndex = (letters: string): number =>
  [...letters].reduce((sum, letter) => sum * 26 + letter.charCodeAt(0) - 64, 0) - 1;

export function readFirstSheet(file: Buffer): { name: string; rows: SheetRows } {
  const entries = unzip(file);
  const get = (name: string): string => {
    const entry = entries.get(name);
    if (!entry) {
      throw new Error(`${name} is not in the file.`);
    }
    return entry.toString('utf8');
  };
  const shared = entries.has('xl/sharedStrings.xml')
    ? [...get('xl/sharedStrings.xml').matchAll(stringItems)].map(match => textOf(match[1] ?? ''))
    : [];
  const name = decode(sheetName.exec(get('xl/workbook.xml'))?.[1] ?? '');
  const result: SheetRows = [];
  for (const row of get('xl/worksheets/sheet1.xml').matchAll(rows)) {
    const texts: string[] = [];
    for (const cell of (row[1] ?? '').matchAll(cells)) {
      const attributes = cell[1] ?? '';
      const body = cell[2] ?? '';
      const column = /\br="([A-Z]+)\d+"/.exec(attributes)?.[1] ?? 'A';
      const type = /\bt="([^"]*)"/.exec(attributes)?.[1];
      const value = cellValue.exec(body)?.[1] ?? '';
      let text = decode(value);
      if (type === 's') {
        text = shared[Number(value)] ?? '';
      } else if (type === 'inlineStr') {
        text = textOf(body);
      }
      texts[columnIndex(column)] = text;
    }
    result.push(Array.from(texts, text => text ?? ''));
  }
  return { name, rows: result };
}
