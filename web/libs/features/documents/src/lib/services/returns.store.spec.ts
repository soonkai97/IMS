import { HttpContext } from '@angular/common/http';
import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of } from 'rxjs';
import { DocumentDto, DocumentsClient, ReturnsClient } from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { ReturnsStore } from './returns.store';

describe('ReturnsStore', () => {
  let spectator: SpectatorService<ReturnsStore>;
  const createService = createServiceFactory({
    service: ReturnsStore,
    providers: [mockProvider(ReturnsClient), mockProvider(DocumentsClient)]
  });

  beforeEach(() => (spectator = createService()));

  it('starts a return of the document and shows a refusal (403, 409) as its own alert', async () => {
    const draft = { id: 'r1' } as DocumentDto;
    spectator.inject(ReturnsClient).create.mockReturnValue(of(draft));

    const answer = await firstValueFrom(spectator.service.start.run('d1'));

    expect(answer).toBe(draft);
    const [body, context] = spectator.inject(ReturnsClient).create.mock.calls[0];
    expect(body).toEqual({ originalDocumentId: 'd1' });
    expect(isHandledInline(context as HttpContext, 409)).toBe(true);
    expect(isHandledInline(context as HttpContext, 403)).toBe(true);
    expect(isHandledInline(context as HttpContext, 500)).toBe(false);
  });

  it('reads the original of a return and forgets both on reset', async () => {
    spectator.inject(DocumentsClient).get.mockReturnValue(of({ id: 'd1' } as DocumentDto));
    spectator.service.original.load('d1');
    await Promise.resolve();
    expect(spectator.service.original.$data()?.id).toBe('d1');

    spectator.service.resetAll();

    expect(spectator.service.original.$data()).toBeUndefined();
  });
});
