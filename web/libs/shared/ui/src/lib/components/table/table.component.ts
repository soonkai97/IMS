import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  contentChildren,
  inject,
  input,
  output
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiScrollRegionDirective } from '../../directives/scroll-region.directive';
import { UiButtonComponent } from '../button/button.component';
import { UiClampedTextComponent } from '../clamped-text/clamped-text.component';
import { UiIconComponent } from '../icon/icon.component';
import { UiSkeletonComponent } from '../skeleton/skeleton.component';
import { UiCellDirective } from './cell.directive';
import {
  TableCell,
  TableColumn,
  TableHeader,
  TableItem,
  TableSort,
  columnTypeOf
} from './table.types';

/**
 * One data definition, two renderings: a table from 1024px up and a card list below (docs/design/mvp-ux.md 2).
 * Loading, error and empty states are part of the component. Put the empty message in `<app-empty-state empty>`.
 */
@Component({
  selector: 'app-table',
  imports: [
    NgTemplateOutlet,
    RouterLink,
    UiButtonComponent,
    UiClampedTextComponent,
    UiIconComponent,
    UiScrollRegionDirective,
    UiSkeletonComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './table.component.html',
  styleUrl: './table.component.scss',
  host: { '[class]': '$hostClass()' }
})
export class UiTableComponent<Row extends object> {
  private readonly uiText = inject(UI_TEXT);

  $columns = input.required<readonly TableColumn<Row>[]>({ alias: 'columns' });
  $rows = input.required<readonly Row[]>({ alias: 'rows' });
  $caption = input.required<string>({ alias: 'caption' });
  $loading = input(false, { alias: 'loading', transform: booleanAttribute });
  $failed = input(false, { alias: 'failed', transform: booleanAttribute });
  $errorMessage = input<string | undefined>(undefined, { alias: 'errorMessage' });
  /** Route of a row; the primary cell becomes a link and the whole row/card is clickable. */
  $rowLink = input<((row: Row) => readonly unknown[] | null) | undefined>(undefined, {
    alias: 'rowLink'
  });

  /** Current server sort, to mark the active header. */
  $sort = input<TableSort | undefined>(undefined, { alias: 'sort' });

  $retried = output<void>({ alias: 'retried' });
  $sortChange = output<TableSort>({ alias: 'sortChange' });

  private readonly $templates = contentChildren(UiCellDirective);

  $text = computed(() => this.uiText());
  $headers = computed<readonly TableHeader[]>(() => {
    const sort = this.$sort();
    return this.$columns().map(column => ({
      key: column.key,
      type: columnTypeOf(column),
      header: column.header,
      alignEnd: column.align === 'end',
      hideOnPhone: column.hideOnPhone === true,
      sortKey: column.sortKey,
      ariaSort: this.ariaSortOf(column.sortKey, sort)
    }));
  });
  $items = computed<readonly TableItem<Row>[]>(() => {
    const templates = new Map(
      this.$templates().map(directive => [directive.$key(), directive.template])
    );
    const columns = this.$columns();
    const rowLink = this.$rowLink();
    return this.$rows().map(row => ({
      row,
      link: rowLink?.(row) ?? null,
      cells: columns.map<TableCell>(column => {
        const text = column.value?.(row) ?? '';
        const template = templates.get(column.key) ?? null;
        return {
          key: column.key,
          type: columnTypeOf(column),
          header: column.header,
          text,
          template,
          primary: column.primary === true,
          alignEnd: column.align === 'end',
          hideOnPhone: column.hideOnPhone === true,
          phoneEnd: column.phoneEnd === true,
          phoneInline: column.phoneInline === true,
          empty: template === null && text.trim() === ''
        };
      })
    }));
  });
  $compact = computed(() =>
    this.$columns().some(column => column.phoneEnd === true || column.phoneInline === true)
  );
  $showSkeleton = computed(() => this.$loading() && this.$items().length === 0);
  $showError = computed(() => this.$failed() && !this.$loading());
  $showEmpty = computed(() => !this.$loading() && !this.$failed() && this.$items().length === 0);
  $refreshing = computed(() => this.$loading() && this.$items().length > 0);
  $hostClass = computed(() => toClassStr(['ui-table', this.$refreshing() && '--refreshing']));

  onSort(header: TableHeader): void {
    const sort = this.$sort();
    if (!header.sortKey) {
      return;
    }
    const desc = sort?.sortBy === header.sortKey ? !sort.desc : false;
    this.$sortChange.emit({ sortBy: header.sortKey, desc });
  }

  onRetry(): void {
    this.$retried.emit();
  }

  private ariaSortOf(
    sortKey: string | undefined,
    sort: TableSort | undefined
  ): TableHeader['ariaSort'] {
    if (!sortKey) {
      return undefined;
    }
    if (sort?.sortBy !== sortKey) {
      return 'none';
    }
    return sort.desc ? 'descending' : 'ascending';
  }
}
