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

  it('shows plain text in a span that carries the full value as its title, so it can be cut with an ellipsis', () => {
    const spectator = render([
      { key: 'partner', header: 'Partner', value: row => row.partner, primary: true }
    ]);

    const text = spectator.query('td .ui-table-text');
    expect(text?.textContent?.trim()).toBe(long);
    expect(text?.getAttribute('title')).toBe(long);
  });
});
