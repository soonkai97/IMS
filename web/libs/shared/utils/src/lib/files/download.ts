import { CSV_BOM, CSV_MIME_TYPE } from '../constants';

export interface DownloadableFile {
  readonly data: Blob;
  readonly fileName?: string;
}

/** Hands a blob to the browser as a download. The server's Content-Disposition name wins over the fallback. */
export function saveFile(file: DownloadableFile, fallbackName: string): void {
  const url = URL.createObjectURL(file.data);
  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName ?? fallbackName;
  link.rel = 'noopener';
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

function escapeCsvCell(value: string | number): string {
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** CSV text with a BOM so Excel opens UTF-8 (Chinese, Malay) correctly. */
export function toCsv(
  headers: readonly string[],
  rows: readonly (readonly (string | number)[])[]
): string {
  const lines = [headers, ...rows].map(row => row.map(escapeCsvCell).join(','));
  return `${CSV_BOM}${lines.join('\r\n')}`;
}

export function saveCsv(csv: string, fileName: string): void {
  saveFile({ data: new Blob([csv], { type: CSV_MIME_TYPE }), fileName }, fileName);
}
