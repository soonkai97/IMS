import { TemplateRef } from '@angular/core';

export interface TableColumn<Row> {
  readonly key: string;
  readonly header: string;
  /** Plain text of the cell. Omit when a `appCell` template renders it. */
  readonly value?: (row: Row) => string;
  readonly align?: 'start' | 'end';
  /** The primary cell is the card title on phones and carries the row link. */
  readonly primary?: boolean;
  /** Hidden when the table collapses to cards. */
  readonly hideOnPhone?: boolean;
  /**
   * Compact card: the cell sits at the end of the title line (a status chip). Any column with this flag or
   * `phoneInline` turns the cards of this table into two lines: title + end cell, then the other cells side by side.
   */
  readonly phoneEnd?: boolean;
  /** Compact card: the value is shown without its label (a date, a name: self-explanatory). */
  readonly phoneInline?: boolean;
  /** Server sort key; makes the header a sort button. */
  readonly sortKey?: string;
}

export interface TableSort {
  readonly sortBy: string;
  readonly desc: boolean;
}

export interface TableCell {
  readonly key: string;
  readonly header: string;
  readonly text: string;
  readonly template: TemplateRef<unknown> | null;
  readonly primary: boolean;
  readonly alignEnd: boolean;
  readonly hideOnPhone: boolean;
  readonly phoneEnd: boolean;
  readonly phoneInline: boolean;
  /** Nothing to show: dropped from the card instead of leaving a blank row. */
  readonly empty: boolean;
}

export interface TableHeader {
  readonly key: string;
  readonly header: string;
  readonly alignEnd: boolean;
  readonly hideOnPhone: boolean;
  readonly sortKey: string | undefined;
  /** aria-sort value for the header cell. */
  readonly ariaSort: 'ascending' | 'descending' | 'none' | undefined;
}

export interface TableItem<Row> {
  readonly row: Row;
  readonly link: readonly unknown[] | null;
  readonly cells: readonly TableCell[];
}

/** Spread into a column list: the column exists only when `condition` holds (a cost or value the user may not see). */
export function columnWhen<Row>(
  condition: boolean,
  column: TableColumn<Row>
): readonly TableColumn<Row>[] {
  return condition ? [column] : [];
}
