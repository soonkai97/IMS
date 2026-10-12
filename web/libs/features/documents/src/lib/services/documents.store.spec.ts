import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of, throwError } from 'rxjs';
import {
  DocumentDto,
  DocumentType,
  DocumentsClient,
  PagedResultOfDocumentListItem
} from '@ims/api';
import { IF_MATCH, isHandledInline } from '@ims/core/http';
import { DocumentsStore, toDocumentRef } from './documents.store';

const DOCUMENT = { id: 7, concurrencyToken: 'AAAA' } as DocumentDto;

/** The context the store hands to the generated client; the If-Match interceptor turns it into the header. */
function tokenOf(context: unknown): string | undefined {
  return (context as HttpContext).get(IF_MATCH);
}

describe('DocumentsStore', () => {
  let spectator: SpectatorService<DocumentsStore>;
  const createService = createServiceFactory({
    service: DocumentsStore,
    providers: [mockProvider(DocumentsClient)]
  });
  const client = () => spectator.inject(DocumentsClient);

  beforeEach(() => {
    spectator = createService();
    for (const action of ['submit', 'approve', 'reject', 'cancel', 'post', 'reverse', 'update']) {
      (client() as unknown as Record<string, jest.Mock>)[action].mockReturnValue(of(DOCUMENT));
    }
  });

  it('lists with the Mine filter and without the other new filters', async () => {
    client().list.mockReturnValue(of({ items: [], page: 1, pageSize: 25, total: 0 }));

    spectator.service.list.load({ type: DocumentType.Issue, page: 1, pageSize: 25, mine: true });
    await Promise.resolve();

    const args = client().list.mock.calls[0];
    expect(args[0]).toBe(DocumentType.Issue);
    expect(args[10]).toBe(true);
    expect(args[11]).toBeUndefined();
  });

  it('loads a document with a call the editor shows in a banner, so a failed load raises no toast (R11)', async () => {
    client().get.mockReturnValue(of(DOCUMENT));

    spectator.service.detail.load(7);
    await Promise.resolve();

    expect(isHandledInline(client().get.mock.calls[0][1] as HttpContext, 500)).toBe(true);
  });

  it('sends the version the user saw with every state change (If-Match)', async () => {
    const ref = toDocumentRef(DOCUMENT);

    await firstValueFrom(spectator.service.submit.run(ref));
    await firstValueFrom(spectator.service.post.run(ref));
    await firstValueFrom(spectator.service.reject.run({ ...ref, reason: 'check it' }));
    await firstValueFrom(spectator.service.cancel.run({ ...ref, reason: '' }));
    await firstValueFrom(spectator.service.reverse.run({ ...ref, reason: 'wrong lot' }));

    expect(tokenOf(client().submit.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().post.mock.calls[0][1])).toBe('AAAA');
    expect(tokenOf(client().reject.mock.calls[0][2])).toBe('AAAA');
    expect(tokenOf(client().cancel.mock.calls[0][2])).toBe('AAAA');
    expect(tokenOf(client().reverse.mock.calls[0][2])).toBe('AAAA');
  });

  it('posts without a request body: the override reason is given at approval', async () => {
    await firstValueFrom(spectator.service.post.run(toDocumentRef(DOCUMENT)));

    expect(client().post).toHaveBeenCalledWith(7, expect.anything());
    expect(client().post.mock.calls[0]).toHaveLength(2);
  });

  it('approves with the override reason and the version', async () => {
    await firstValueFrom(
      spectator.service.approve.run({
        ...toDocumentRef(DOCUMENT),
        negativeStockOverrideReason: 'Customer pre-paid'
      })
    );

    const [id, body, context] = client().approve.mock.calls[0];
    expect(id).toBe(7);
    expect(body).toEqual({ negativeStockOverrideReason: 'Customer pre-paid' });
    expect(tokenOf(context)).toBe('AAAA');
  });

  it('approves a document without an override with an empty reason', async () => {
    await firstValueFrom(
      spectator.service.approve.run({ ...toDocumentRef(DOCUMENT), negativeStockOverrideReason: '' })
    );

    expect(client().approve.mock.calls[0][1]).toEqual({ negativeStockOverrideReason: undefined });
  });

  it('saves an existing draft with its version and a new one without', async () => {
    client().createReceipt.mockReturnValue(of(DOCUMENT));
    const body = { warehouseId: 1, lines: [] } as never;

    await firstValueFrom(
      spectator.service.save.run({ type: DocumentType.Receipt, ref: toDocumentRef(DOCUMENT), body })
    );
    await firstValueFrom(spectator.service.save.run({ type: DocumentType.Receipt, body }));

    expect(tokenOf(client().update.mock.calls[0][2])).toBe('AAAA');
    expect(client().createReceipt).toHaveBeenCalledTimes(1);
  });

  it('reads the current token of a document from the list, matching by id', async () => {
    client().list.mockReturnValue(
      of({
        items: [
          { id: 70, concurrencyToken: 'OTHER' },
          { id: 7, concurrencyToken: 'BBBB' }
        ],
        page: 1,
        pageSize: 5,
        total: 2
      } as PagedResultOfDocumentListItem)
    );

    const token = await firstValueFrom(
      spectator.service.currentToken({ id: 7, no: 'RCV-7', type: DocumentType.Receipt })
    );

    expect(token).toBe('BBBB');
    expect(client().list.mock.calls[0][5]).toBe('RCV-7');
  });

  it('keeps the answer of every command as the opened document and forgets outcomes on reset', async () => {
    await firstValueFrom(spectator.service.submit.run(toDocumentRef(DOCUMENT)));

    expect(spectator.service.detail.$data()).toBe(DOCUMENT);

    spectator.service.resetCommands();
    expect(spectator.service.submit.$operation().error).toBeUndefined();
  });

  it('a command that changes the document clears what the others failed with (a stale "Stock changed" banner)', async () => {
    const stockConflict = new Error('Stock changed');
    client().post.mockReturnValue(throwError(() => stockConflict));
    await firstValueFrom(spectator.service.post.run(toDocumentRef(DOCUMENT)), {
      defaultValue: undefined
    });
    expect(spectator.service.post.$isFailed()).toBe(true);

    await firstValueFrom(spectator.service.cancel.run({ ...toDocumentRef(DOCUMENT), reason: '' }));

    expect(spectator.service.post.$isFailed()).toBe(false);
    expect(spectator.service.cancel.$isFailed()).toBe(false);
  });

  it('a failed command leaves the others alone', async () => {
    client().post.mockReturnValue(throwError(() => new Error('Stock changed')));
    await firstValueFrom(spectator.service.submit.run(toDocumentRef(DOCUMENT)));
    await firstValueFrom(spectator.service.post.run(toDocumentRef(DOCUMENT)), {
      defaultValue: undefined
    });

    expect(spectator.service.post.$isFailed()).toBe(true);
    expect(spectator.service.submit.$isFailed()).toBe(false);
  });
});
