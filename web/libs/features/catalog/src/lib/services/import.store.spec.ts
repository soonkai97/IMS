import { createServiceFactory, mockProvider, SpectatorService } from '@ngneat/spectator/jest';
import { firstValueFrom, of } from 'rxjs';
import { ImportClient, ImportResult } from '@ims/api';
import { IMPORT_KIND } from '../constants/import.const';
import { ImportStore } from './import.store';

const RESULT = {
  committed: false,
  total: 1,
  valid: 1,
  invalid: 0,
  created: 1,
  updated: 0,
  rows: []
} as unknown as ImportResult;

describe('ImportStore', () => {
  let spectator: SpectatorService<ImportStore>;
  const createService = createServiceFactory({
    service: ImportStore,
    providers: [mockProvider(ImportClient)]
  });
  const client = () => spectator.inject(ImportClient);
  const file = new File(['x'], 'products.xlsx');

  beforeEach(() => {
    spectator = createService();
    client().importProducts.mockReturnValue(of(RESULT));
    client().importOpeningStock.mockReturnValue(of(RESULT));
  });

  it('previews products with commit=false and keeps the result', async () => {
    await firstValueFrom(
      spectator.service.run.run({ kind: IMPORT_KIND.Products, file, commit: false })
    );

    expect(client().importProducts).toHaveBeenCalledWith(false, {
      data: file,
      fileName: 'products.xlsx'
    });
    expect(spectator.service.$result()).toBe(RESULT);
  });

  it('commits opening stock through its own operation', async () => {
    await firstValueFrom(
      spectator.service.run.run({ kind: IMPORT_KIND.OpeningStock, file, commit: true })
    );

    expect(client().importOpeningStock).toHaveBeenCalledWith(true, expect.anything());
    expect(client().importProducts).not.toHaveBeenCalled();
  });
});
