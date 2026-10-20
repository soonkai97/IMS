import { provideRouter } from '@angular/router';
import { createComponentFactory, mockProvider, Spectator } from '@ngneat/spectator/jest';
import { ImportClient, ImportResult, ImportRowStatus } from '@ims/api';
import { ModulesStore } from '@ims/core/identity';
import { ImportStore } from '@ims/features/catalog';
import { ProductImportPageComponent } from './product-import.component';

const PREVIEW: ImportResult = {
  committed: false,
  total: 3,
  valid: 2,
  invalid: 1,
  created: 1,
  updated: 1,
  documentId: undefined,
  rows: [
    { row: 2, key: 'NEW-1', ok: true, status: ImportRowStatus.New, errors: [] },
    {
      row: 3,
      key: 'OLD-1',
      ok: true,
      status: ImportRowStatus.Update,
      errors: ['Unit not changed.']
    },
    { row: 4, key: 'BAD-1', ok: false, status: ImportRowStatus.Error, errors: ['SKU is required.'] }
  ]
};

let accountingOn = false;
const modulesProvider = { provide: ModulesStore, useValue: { isEnabled: () => accountingOn } };

describe('ProductImportPageComponent preview', () => {
  let spectator: Spectator<ProductImportPageComponent>;
  const createComponent = createComponentFactory({
    component: ProductImportPageComponent,
    providers: [provideRouter([]), mockProvider(ImportClient), modulesProvider]
  });

  const text = (): string => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';

  beforeEach(() => {
    spectator = createComponent();
    spectator.inject(ImportStore).$result.set(PREVIEW);
    spectator.component.errorsOnlyControl.setValue(false);
    spectator.detectChanges();
  });

  it('summarises what a commit would do: new, update and error counts', () => {
    expect(text()).toContain('3 rows | 1 new | 1 will update | 1 with errors');
  });

  it('labels every row New, Update or Error (not just OK or Error)', () => {
    const cells = spectator
      .queryAll('app-status-chip')
      .map(chip => (chip.textContent ?? '').trim());

    expect(cells).toEqual(['New', 'Update', 'Error']);
  });

  it('keeps the notes of a valid row and blocks the confirm button while rows have errors', () => {
    expect(text()).toContain('Unit not changed.');
    const confirm = spectator
      .queryAll('button')
      .find(button => (button.textContent ?? '').includes('Confirm import'));
    expect(confirm?.hasAttribute('disabled')).toBe(true);
  });
});

describe('ProductImportPageComponent, optional tax columns (module accounting)', () => {
  let spectator: Spectator<ProductImportPageComponent>;
  const createComponent = createComponentFactory({
    component: ProductImportPageComponent,
    providers: [provideRouter([]), mockProvider(ImportClient), modulesProvider]
  });
  const text = (): string => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';

  it('says nothing about HsCode or SstCode while the module is off (the API refuses a file that has them)', () => {
    accountingOn = false;
    spectator = createComponent();

    expect(text()).not.toContain('HsCode');
    expect(text()).not.toContain('SstCode');
  });

  it('names the two optional columns, their shape and the Text format while the module is on', () => {
    accountingOn = true;
    spectator = createComponent();

    expect(text()).toContain('HsCode (digits and dots, up to 16 characters)');
    expect(text()).toContain('SstCode (letters, digits and hyphens, up to 20)');
    expect(text()).toContain('Format both columns as Text');
  });

  it('does not mention them on the opening-stock tab, which has no such columns', () => {
    accountingOn = true;
    spectator = createComponent();

    spectator.component.onKindChange('opening-stock');
    spectator.detectChanges();

    expect(text()).not.toContain('HsCode');
  });
});
