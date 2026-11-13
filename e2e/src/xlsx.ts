import { inflateRawSync } from 'node:zlib';

/**
 * A just-enough reader for the .xlsx files the API writes (a zip of XML parts; the suite adds no spreadsheet library for it).
 * Returns the first sheet as rows of text, a missing cell as ''. Not a general reader: no formulas, dates are the raw serial.
 */
export function readFirstSheet(file: Buffer): string[][] {
  const parts = unzip(file);
  // The writer prefixes its elements (<x:row>): the prefix is dropped so the patterns below read plain names.
  const plain = (name: string): string | undefined =>
    parts.get(name)?.replace(/<(\/?)\w+:/g, '<$1');
  const sheet = plain('xl/worksheets/sheet1.xml');
  if (sheet === undefined) {
    throw new Error('The workbook has no first sheet.');
  }
  const shared = [
    ...(plain('xl/sharedStrings.xml') ?? '').matchAll(/<si\b[^>]*>([\s\S]*?)<\/si>/g)
  ].map(match => textOf(match[1]!));
  const rows: string[][] = [];
  for (const row of sheet.matchAll(/<row\b[^>]*>([\s\S]*?)<\/row>/g)) {
    const cells: string[] = [];
    for (const cell of row[1]!.matchAll(/<c\b([^>]*?)(?:\/>|>([\s\S]*?)<\/c>)/g)) {
      const attributes = cell[1]!;
      const column = columnIndex(/\br="([A-Z]+)\d+"/.exec(attributes)?.[1] ?? 'A');
      const type = /\bt="([^"]+)"/.exec(attributes)?.[1];
      const body = cell[2] ?? '';
      const value = /<v>([\s\S]*?)<\/v>/.exec(body)?.[1];
      cells[column] =
        type === 's'
          ? (shared[Number(value)] ?? '')
          : type === 'inlineStr'
            ? textOf(body)
            : decode(value ?? '');
    }
    rows.push(Array.from(cells, cell => cell ?? ''));
  }
  return rows;
}

const columnIndex = (letters: string): number =>
  [...letters].reduce((sum, letter) => sum * 26 + letter.charCodeAt(0) - 64, 0) - 1;

const textOf = (xml: string): string =>
  [...xml.matchAll(/<t\b[^>]*>([\s\S]*?)<\/t>/g)].map(match => decode(match[1]!)).join('');

const decode = (text: string): string =>
  text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&');

/** Every stored or deflated entry of a zip, by name, read through the central directory (sizes there are always right). */
function unzip(file: Buffer): Map<string, string> {
  let end = file.length - 22;
  while (end >= 0 && file.readUInt32LE(end) !== 0x06054b50) {
    end--;
  }
  if (end < 0) {
    throw new Error('Not a zip file.');
  }
  const count = file.readUInt16LE(end + 10);
  let at = file.readUInt32LE(end + 16);
  const parts = new Map<string, string>();
  for (let index = 0; index < count; index++) {
    const method = file.readUInt16LE(at + 10);
    const size = file.readUInt32LE(at + 20);
    const nameLength = file.readUInt16LE(at + 28);
    const extraLength = file.readUInt16LE(at + 30);
    const commentLength = file.readUInt16LE(at + 32);
    const local = file.readUInt32LE(at + 42);
    const name = file.toString('utf8', at + 46, at + 46 + nameLength);
    const start = local + 30 + file.readUInt16LE(local + 26) + file.readUInt16LE(local + 28);
    const data = file.subarray(start, start + size);
    parts.set(name, (method === 8 ? inflateRawSync(data) : data).toString('utf8'));
    at += 46 + nameLength + extraLength + commentLength;
  }
  return parts;
}
