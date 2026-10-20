import { HttpContext } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { AccountingClient, FileResponse } from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { AccountingExportStore } from './accounting-export.store';

describe('AccountingExportStore', () => {
  const file = (name?: string): FileResponse => ({
    data: new Blob(['x']),
    status: 200,
    fileName: name
  });
  const exportCall = jest.fn();
  let store: AccountingExportStore;
  const created: string[] = [];
  const downloads: string[] = [];

  beforeEach(() => {
    exportCall.mockReset();
    created.length = 0;
    downloads.length = 0;
    // jsdom has no object URLs and does not navigate; record what would have been downloaded.
    URL.createObjectURL = jest.fn(() => 'blob:test');
    URL.revokeObjectURL = jest.fn();
    jest.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
      this: HTMLAnchorElement
    ) {
      downloads.push(this.download);
    });
    TestBed.configureTestingModule({
      providers: [{ provide: AccountingClient, useValue: { export: exportCall } }]
    });
    store = TestBed.inject(AccountingExportStore);
  });

  afterEach(() => jest.restoreAllMocks());

  it('asks for the period and the warehouse, shows its own failures (no toast) and hands over the file under the server name', () => {
    const answer = new Subject<FileResponse>();
    exportCall.mockReturnValue(answer);

    store.download.run({ from: '2026-10-01', to: '2026-10-31', warehouseId: 'w1' }).subscribe();
    answer.next(file('ims-accounting-2026-10-01-2026-10-31.xlsx'));
    answer.complete();

    const [from, to, warehouseId, context] = exportCall.mock.calls[0] as [
      string,
      string,
      string,
      HttpContext
    ];
    expect([from, to, warehouseId]).toEqual(['2026-10-01', '2026-10-31', 'w1']);
    expect(isHandledInline(context, 400)).toBe(true);
    expect(isHandledInline(context, 404)).toBe(true);
    expect(downloads).toEqual(['ims-accounting-2026-10-01-2026-10-31.xlsx']);
    expect(store.$lastFileName()).toBe('ims-accounting-2026-10-01-2026-10-31.xlsx');
  });

  it('falls back to a fixed name when the server sent none', () => {
    exportCall.mockReturnValue(new Subject<FileResponse>());
    const answer = new Subject<FileResponse>();
    exportCall.mockReturnValue(answer);

    store.download.run({ from: '2026-10-01', to: '2026-10-31' }).subscribe();
    answer.next(file());

    expect(downloads).toEqual(['ims-accounting.xlsx']);
  });

  it('a double click sends one request: the second call while the first runs is dropped', () => {
    const answer = new Subject<FileResponse>();
    exportCall.mockReturnValue(answer);

    store.download.run({ from: '2026-10-01', to: '2026-10-31' }).subscribe();
    store.download.run({ from: '2026-10-01', to: '2026-10-31' }).subscribe();
    answer.next(file('a.xlsx'));
    answer.complete();

    expect(exportCall).toHaveBeenCalledTimes(1);
    expect(downloads).toEqual(['a.xlsx']);
  });

  it('a failure hands over no file and is kept on the command for the screen to show', () => {
    const answer = new Subject<FileResponse>();
    exportCall.mockReturnValue(answer);

    store.download.run({ from: '2026-10-01', to: '2026-10-31' }).subscribe();
    answer.error({ isApiError: true, status: 400, message: 'Too many rows', fieldErrors: {} });

    expect(downloads).toEqual([]);
    expect(store.download.$operation().error?.message).toBe('Too many rows');
    expect(store.download.$isRunning()).toBe(false);
  });
});
