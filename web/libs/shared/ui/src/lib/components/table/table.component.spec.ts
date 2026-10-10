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
