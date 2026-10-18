import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  contentChildren,
  DestroyRef,
  ElementRef,
  inject,
  input,
  output,
  signal,
  viewChild
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiPublishHeightDirective } from '../../directives/publish-height.directive';
import { UiScrollRegionDirective } from '../../directives/scroll-region.directive';
import { UiButtonComponent } from '../button/button.component';
import { UiCheckboxComponent } from '../checkbox/checkbox.component';
import { UiClampedTextComponent } from '../clamped-text/clamped-text.component';
import { UiIconComponent } from '../icon/icon.component';
import { UiSkeletonComponent } from '../skeleton/skeleton.component';
import { UiCellDirective } from './cell.directive';
import {
  TableCell,
  TableColumn,
  TableHeader,
  TableItem,
  TableMenuColumn,
  TableSort,
  columnTypeOf,
  isColumnLocked
} from './table.types';

let nextMenuId = 0;
/** Space between the button and its menu. */
const MENU_GAP_PX = 4;
const NO_KEYS: ReadonlySet<string> = new Set();

/**
 * One data definition, two renderings, chosen by the width of the table's own box: a table from 45rem up (header and first
 * column stay in view while it scrolls) and a card list below (docs/design/mvp-ux.md 2, A02).
 * Loading, error and empty states are part of the component. Put the empty message in `<app-empty-state empty>`.
 * `columnMenu` adds a "Columns" button above a wide table that hides and shows columns (a native popover: Esc and a click outside
 * close it). The identifier column and action columns are always shown; a card list always shows every column.
 */
@Component({
  selector: 'app-table',
  imports: [
    FormsModule,
    NgTemplateOutlet,
    RouterLink,
    UiButtonComponent,
    UiCheckboxComponent,
    UiClampedTextComponent,
    UiIconComponent,
    UiPublishHeightDirective,
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
  private readonly menu = viewChild<ElementRef<HTMLElement>>('menu');
  private readonly menuButton = viewChild<ElementRef<HTMLElement>>('menuButton');
  private menuListeners: AbortController | undefined;

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

  /** Offers the "Columns" menu above the table (wide tables only; the cards of a narrow table show everything). */
  $columnMenu = input(false, { alias: 'columnMenu', transform: booleanAttribute });

  /** Current server sort, to mark the active header. */
  $sort = input<TableSort | undefined>(undefined, { alias: 'sort' });

  $retried = output<void>({ alias: 'retried' });
  $sortChange = output<TableSort>({ alias: 'sortChange' });

  private readonly $templates = contentChildren(UiCellDirective);

  /** Keys the user switched off. A key no column has any more (another language, another role) is simply never matched. */
  private readonly $hidden = signal<ReadonlySet<string>>(new Set());

  $text = computed(() => this.uiText());
  /** What the menu lists: every column but the identifier and the action columns. */
  $menuColumns = computed<readonly TableMenuColumn[]>(() => {
    if (!this.$columnMenu()) {
      return [];
    }
    const hidden = this.$hidden();
    return this.$columns()
      .filter(column => !isColumnLocked(column))
      .map(column => ({
        key: column.key,
        header: column.header,
        visible: !hidden.has(column.key)
      }));
  });
  $hiddenCount = computed(() => this.$menuColumns().filter(column => !column.visible).length);
  $menuId = `ui-table-menu-${nextMenuId++}`;
  $headers = computed<readonly TableHeader[]>(() => {
    const sort = this.$sort();
    const hidden = this.hiddenKeys();
    return this.$columns().map(column => ({
      key: column.key,
      type: columnTypeOf(column),
      header: column.header,
      alignEnd: column.align === 'end',
      hideOnPhone: column.hideOnPhone === true,
      hidden: hidden.has(column.key),
      sortKey: column.sortKey,
      ariaSort: this.ariaSortOf(column.sortKey, sort)
    }));
  });
  $items = computed<readonly TableItem<Row>[]>(() => {
    const templates = new Map(
      this.$templates().map(directive => [directive.$key(), directive.template])
    );
    const columns = this.$columns();
    const hidden = this.hiddenKeys();
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
          hidden: hidden.has(column.key),
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

  constructor() {
    inject(DestroyRef).onDestroy(() => this.menuListeners?.abort());
  }

  onColumnToggle(key: string, visible: boolean): void {
    this.$hidden.update(hidden => {
      const next = new Set(hidden);
      if (visible) {
        next.delete(key);
      } else {
        next.add(key);
      }
      return next;
    });
  }

  onShowAllColumns(): void {
    this.$hidden.set(new Set());
  }

  /** Tab out of the menu closes it: a popover that stays open behind the focus would be left hanging. */
  onMenuFocusOut(event: FocusEvent): void {
    const menu = this.menu()?.nativeElement;
    const next = event.relatedTarget;
    // The Columns button is not "elsewhere": its own click closes the menu, and a close here would let that click reopen it.
    if (
      menu &&
      next instanceof Node &&
      !menu.contains(next) &&
      next !== this.menuButton()?.nativeElement
    ) {
      menu.hidePopover();
    }
  }

  /** A popover sits in the top layer at fixed coordinates: put it under its button, and close it when the page moves. */
  onMenuToggle(event: Event): void {
    const menu = this.menu()?.nativeElement;
    const button = this.menuButton()?.nativeElement;
    if (!menu || !button) {
      return;
    }
    this.menuListeners?.abort();
    if ((event as ToggleEvent).newState !== 'open') {
      return;
    }
    const box = button.getBoundingClientRect();
    menu.style.top = `${box.bottom + MENU_GAP_PX}px`;
    menu.style.right = `${document.documentElement.clientWidth - box.right}px`;
    menu.querySelector('input')?.focus();
    this.menuListeners = new AbortController();
    const close = (): void => menu.hidePopover();
    const { signal } = this.menuListeners;
    window.addEventListener('scroll', close, { signal, passive: true });
    window.addEventListener('resize', close, { signal });
  }

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

  /** Keys hidden by the user; nothing is hidden when the menu is not offered. */
  private hiddenKeys(): ReadonlySet<string> {
    return this.$columnMenu() ? this.$hidden() : NO_KEYS;
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
