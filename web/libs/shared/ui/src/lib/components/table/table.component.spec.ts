import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiTableComponent } from './table.component';
import { TableColumn } from './table.types';

interface Row {
  readonly no: string;
  readonly partner: string;
  readonly status: string;
}

describe('UiTableComponent card layout', () => {
  let spectator: Spectator<UiTableComponent<Row>>;
  const createComponent = createComponentFactory({
    component: UiTableComponent<Row>,
    providers: [provideRouter([])],
    detectChanges: false
  });

  const rows: Row[] = [
    { no: 'RCV-1', partner: 'Acme', status: 'Draft' },
    { no: 'RCV-2', partner: '', status: 'Posted' }
  ];

  function render(columns: TableColumn<Row>[]): void {
    spectator = createComponent({
      props: { columns, rows, caption: 'Receipts' } as never
    });
    spectator.detectChanges();
  }

  const plain: TableColumn<Row>[] = [
    { key: 'no', header: 'No.', value: row => row.no, primary: true },
    { key: 'partner', header: 'Partner', value: row => row.partner },
    { key: 'status', header: 'Status', value: row => row.status }
  ];

  it('stays a labelled card unless a column asks for the compact layout', () => {
    render(plain);

    expect(spectator.queryAll('tr.ui-table-row.--compact')).toHaveLength(0);
  });

  it('NV-08: compact cards put the chip on the title line and drop labels and blank cells', () => {
    render([plain[0], { ...plain[1], phoneInline: true }, { ...plain[2], phoneEnd: true }]);

    expect(spectator.queryAll('tr.ui-table-row.--compact')).toHaveLength(2);
    const second = spectator.queryAll('tr.ui-table-row')[1];
    const partnerCell = second.querySelector('[data-label="Partner"]');
    // A document without a partner gets no empty line.
    expect(partnerCell?.classList).toContain('--empty');
    const statusCell = second.querySelector('[data-label="Status"]');
    expect(statusCell?.classList).toContain('--phone-end');
    const firstPartner = spectator
      .queryAll('tr.ui-table-row')[0]
      .querySelector('td.--phone-inline');
    expect(firstPartner?.textContent?.trim()).toBe('Acme');
  });
});

describe('UiTableComponent structure (A02)', () => {
  const rows: Row[] = [{ no: 'RCV-1', partner: 'Acme', status: 'Draft' }];
  const createComponent = createComponentFactory({
    component: UiTableComponent<Row>,
    providers: [provideRouter([])],
    detectChanges: false
  });
  const columns: TableColumn<Row>[] = [
    { key: 'no', header: 'No.', value: row => row.no, primary: true, sortKey: 'no' },
    { key: 'partner', header: 'Partner', value: row => row.partner },
    { key: 'status', header: 'Status', value: row => row.status, type: 'status' }
  ];

  function render(props: Record<string, unknown> = {}): Spectator<UiTableComponent<Row>> {
    const spectator = createComponent({
      props: { columns, rows, caption: 'Receipts', ...props } as never
    });
    spectator.detectChanges();
    return spectator;
  }

  it('is one native table: a caption, a header row of column headers, then the body rows', () => {
    const spectator = render();

    expect(spectator.query('table > caption')?.textContent?.trim()).toBe('Receipts');
    const headers = spectator.queryAll('table > thead > tr > th');
    expect(headers.map(th => th.getAttribute('scope'))).toEqual(['col', 'col', 'col']);
    expect(spectator.queryAll('table > tbody > tr.ui-table-row')).toHaveLength(1);
  });

  it('every cell carries its column header, so a card (which hides the header row) can show it', () => {
    const spectator = render();

    expect(spectator.queryAll('td').map(td => td.getAttribute('data-label'))).toEqual([
      'No.',
      'Partner',
      'Status'
    ]);
  });

  it('the sort state is in aria-sort on the header and a real button inside it', () => {
    const spectator = render({ sort: { sortBy: 'no', desc: true } });

    const first = spectator.query('th');
    expect(first?.getAttribute('aria-sort')).toBe('descending');
    expect(first?.querySelector('button.ui-table-sort')).toBeTruthy();
    expect(spectator.queryAll('th')[1].hasAttribute('aria-sort')).toBe(false);
  });

  it('shows no header over an empty table (TB-06): the empty state replaces the whole grid', () => {
    const spectator = render({ rows: [] });

    expect(spectator.query('table')).toBeNull();
    expect(spectator.query('th')).toBeNull();
  });

  it('is the scroll box that only a table wider than its room scrolls in: no role and no tab stop while it fits', () => {
    const spectator = render();

    const box = spectator.query('.ui-table-scroll');
    expect(box?.querySelector('table.ui-table-grid')).toBeTruthy();
    expect(box?.hasAttribute('data-scroll-region')).toBe(false);
  });
});

describe('UiTableComponent long values (L00)', () => {
  const long = 'W'.repeat(300);
  const rows: Row[] = [{ no: 'RCV-1', partner: long, status: 'Draft' }];
  const createComponent = createComponentFactory({
    component: UiTableComponent<Row>,
    providers: [provideRouter([])],
    detectChanges: false
  });

  function render(columns: TableColumn<Row>[]): Spectator<UiTableComponent<Row>> {
    const spectator = createComponent({ props: { columns, rows, caption: 'Receipts' } as never });
    spectator.detectChanges();
    return spectator;
  }

  it('gives every column a type class: declared, else number when aligned to the end, name when primary, text otherwise', () => {
    const spectator = render([
      { key: 'no', header: 'No.', value: row => row.no, primary: true, type: 'code' },
      { key: 'partner', header: 'Partner', value: row => row.partner },
      { key: 'qty', header: 'Qty', value: () => '3', align: 'end' },
      { key: 'name', header: 'Name', value: row => row.no, primary: true }
    ]);

    const headers = spectator.queryAll('th').map(th => th.className);
    expect(headers[0]).toContain('--code');
    expect(headers[1]).toContain('--text');
    expect(headers[2]).toContain('--number');
    expect(headers[3]).toContain('--name');
    expect(spectator.query('td.--code')).toBeTruthy();
  });

  it('puts name and text cells in app-clamped-text, which holds the full value, and leaves codes as plain text', () => {
    const spectator = render([
      { key: 'no', header: 'No.', value: row => row.no, type: 'code' },
      { key: 'partner', header: 'Partner', value: row => row.partner }
    ]);

    expect(spectator.query('td.--code app-clamped-text')).toBeNull();
    expect(spectator.query('td.--code')?.textContent?.trim()).toBe('RCV-1');
    expect(spectator.query('td.--text app-clamped-text')?.textContent?.trim()).toBe(long);
  });

  it('only a title that links to its detail page is not expandable in place', () => {
    const spectator = createComponent({
      props: {
        columns: [
          { key: 'partner', header: 'Partner', value: (row: Row) => row.partner, primary: true }
        ],
        rows,
        caption: 'Receipts',
        rowLink: () => ['/receipts', 1]
      } as never
    });
    spectator.detectChanges();

    expect(spectator.query('a.ui-table-link app-clamped-text')).toBeTruthy();
    expect(spectator.query('app-clamped-text button')).toBeNull();
  });

  it('sits in a scroll box that is a region and a tab stop only while it really scrolls', () => {
    const spectator = render([{ key: 'no', header: 'No.', value: row => row.no, type: 'code' }]);

    // jsdom lays nothing out, so the table fits: no landmark and no extra tab stop.
    const box = spectator.query('.ui-table-scroll');
    expect(box).toBeTruthy();
    expect(box?.hasAttribute('role')).toBe(false);
    expect(box?.hasAttribute('tabindex')).toBe(false);
  });
});

describe('UiTableComponent column menu (TB)', () => {
  const rows: Row[] = [{ no: 'RCV-1', partner: 'Acme', status: 'Draft' }];
  const createComponent = createComponentFactory({
    component: UiTableComponent<Row>,
    providers: [provideRouter([])],
    detectChanges: false
  });
  const columns: TableColumn<Row>[] = [
    { key: 'no', header: 'No.', value: row => row.no, primary: true },
    { key: 'partner', header: 'Partner', value: row => row.partner },
    { key: 'status', header: 'Status', value: row => row.status, type: 'status' },
    { key: 'open', header: 'Open', type: 'action' }
  ];

  function render(props: Record<string, unknown> = {}): Spectator<UiTableComponent<Row>> {
    const spectator = createComponent({
      props: { columns, rows, caption: 'Receipts', columnMenu: true, ...props } as never
    });
    spectator.detectChanges();
    return spectator;
  }

  const items = (spectator: Spectator<UiTableComponent<Row>>): string[] =>
    spectator.queryAll('.ui-table-menu app-checkbox').map(item => item.textContent?.trim() ?? '');

  it('offers no menu unless asked, so existing tables are unchanged', () => {
    const spectator = render({ columnMenu: false });

    expect(spectator.query('.ui-table-menu')).toBeNull();
    expect(spectator.query('.ui-table-menu-button')).toBeNull();
  });

  it('is a button that opens a native popover, named Columns', () => {
    const spectator = render();
    const button = spectator.query('.ui-table-menu-button') as HTMLElement;
    const menu = spectator.query('.ui-table-menu') as HTMLElement;

    expect(button.textContent?.trim()).toBe('Columns');
    expect(button.getAttribute('popovertarget')).toBe(menu.id);
    expect(menu.hasAttribute('popover')).toBe(true);
    expect(menu.getAttribute('role')).toBe('group');
    expect(menu.getAttribute('aria-label')).toBe('Columns');
  });

  it('lists every column but the identifier and the action column', () => {
    const spectator = render();

    expect(items(spectator)).toEqual(['Partner', 'Status']);
  });

  it('hides a column header and its cells when it is switched off, and shows it again', () => {
    const spectator = render();
    const hidden = (): string[] =>
      spectator.queryAll('th.--user-hidden').map(header => header.textContent?.trim() ?? '');

    spectator.component.onColumnToggle('partner', false);
    spectator.detectChanges();

    expect(hidden()).toEqual(['Partner']);
    expect(spectator.queryAll('td.--user-hidden')).toHaveLength(1);
    expect(spectator.query('td.--user-hidden')?.getAttribute('data-label')).toBe('Partner');

    spectator.component.onColumnToggle('partner', true);
    spectator.detectChanges();

    expect(hidden()).toEqual([]);
  });

  it('Show all columns brings every column back, and stays on the page (aria-disabled) while nothing is hidden', () => {
    const spectator = render();
    const reset = spectator.query('.ui-table-menu-reset') as HTMLElement;
    expect(reset.getAttribute('aria-disabled')).toBe('true');

    spectator.component.onColumnToggle('partner', false);
    spectator.component.onColumnToggle('status', false);
    spectator.detectChanges();
    expect(reset.hasAttribute('aria-disabled')).toBe(false);

    spectator.click(reset);

    expect(spectator.queryAll('th.--user-hidden')).toHaveLength(0);
  });

  it('ignores a hidden key the columns no longer have', () => {
    const spectator = render();
    spectator.component.onColumnToggle('gone', false);
    spectator.detectChanges();

    expect(spectator.queryAll('th.--user-hidden')).toHaveLength(0);
    expect(items(spectator)).toEqual(['Partner', 'Status']);
  });

  it('offers no menu when every column is locked', () => {
    const spectator = render({ columns: [columns[0], columns[3]] });

    expect(spectator.query('.ui-table-toolbar')).toBeNull();
  });

  it('does not claim a menu popup: the popover is a group of checkboxes', () => {
    const spectator = render();

    expect(spectator.query('.ui-table-menu-button')?.hasAttribute('aria-haspopup')).toBe(false);
  });

  it('closes when the focus leaves it, but not for its own button (that click toggles it)', () => {
    const spectator = render();
    const menu = spectator.query('.ui-table-menu') as HTMLElement;
    const hide = jest.fn();
    menu.hidePopover = hide;
    const outside = document.createElement('button');
    document.body.append(outside);

    menu.dispatchEvent(new FocusEvent('focusout', { relatedTarget: menu.querySelector('input') }));
    menu.dispatchEvent(
      new FocusEvent('focusout', { relatedTarget: spectator.query('.ui-table-menu-button') })
    );
    menu.dispatchEvent(new FocusEvent('focusout', { relatedTarget: null }));
    expect(hide).not.toHaveBeenCalled();

    menu.dispatchEvent(new FocusEvent('focusout', { relatedTarget: outside }));
    expect(hide).toHaveBeenCalledTimes(1);
    outside.remove();
  });
});
