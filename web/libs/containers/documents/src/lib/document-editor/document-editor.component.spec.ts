import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { of } from 'rxjs';
import { DocumentDto, DocumentStatus, DocumentType, IssuePurpose } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { ConfirmService } from '@ims/core/notification';
import { SystemSettingsStore } from '@ims/core/settings';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import { DocumentsStore, StockAvailabilityService } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  DOCUMENT_ACTION,
  PERMISSION,
  failureOperation
} from '@ims/models';
import { ApiCommand } from '@ims/utils';
import { DocumentEditorPageComponent } from './document-editor.component';

function document(overrides: Partial<DocumentDto> = {}): DocumentDto {
  return {
    id: 2,
    type: DocumentType.Issue,
    no: 'ISS-WH01-2610-0001',
    status: DocumentStatus.Submitted,
    warehouseId: 1,
    warehouseName: 'Main warehouse',
    partnerId: undefined,
    partnerName: undefined,
    purpose: IssuePurpose.Sale,
    remark: undefined,
    requiresApproval: true,
    requiresOverride: false,
    isLarge: false,
    isReturned: false,
    rejectionReason: undefined,
    cancelReason: undefined,
    reversalReason: undefined,
    negativeStockOverrideReason: undefined,
    createdById: 3,
    createdBy: 'staff',
    createdByName: 'Staff',
    createdAt: '2026-10-09T01:00:00Z',
    submittedById: 3,
    submittedBy: 'staff',
    submittedByName: 'Staff',
    submittedAt: '2026-10-09T02:00:00Z',
    approvedBy: undefined,
    approvedByName: undefined,
    approvedAt: undefined,
    postedBy: undefined,
    postedByName: undefined,
    postedAt: undefined,
    returnedById: undefined,
    returnedBy: undefined,
    returnedByName: undefined,
    returnedAt: undefined,
    totalQty: 15,
    totalAmount: 0,
    lines: [
      {
        id: 9,
        productId: 1,
        sku: 'DEMO-001',
        productName: 'Demo widget',
        qty: 15,
        uomId: 1,
        uomCode: 'PCS',
        baseQty: 15,
        unitCost: 0,
        remark: undefined,
        available: 10
      }
    ],
    allowedActions: [DOCUMENT_ACTION.Approve, DOCUMENT_ACTION.Reject],
    concurrencyToken: 'AAAA',
    ...overrides
  };
}

const STALE: ApiError = {
  isApiError: true,
  kind: API_ERROR_KIND.Conflict,
  status: 409,
  title: 'Conflict',
  detail: 'ISS-WH01-2610-0001 was changed by someone else since you opened it.',
  fieldErrors: {},
  extensions: {}
};

describe('DocumentEditorPageComponent', () => {
  let spectator: Spectator<DocumentEditorPageComponent>;
  const opened = signal<DocumentDto | undefined>(undefined);
  const calls: Record<string, unknown[]> = {};
  const detailReload = jest.fn();
  const currentToken = jest.fn();
  const resetCommands = jest.fn();
  let commands: Record<string, ApiCommand<never, DocumentDto>>;

  /** One recording command per action; each answers with the opened document. */
  function command(name: string): ApiCommand<never, DocumentDto> {
    return new ApiCommand<never, DocumentDto>(arg => {
      (calls[name] ??= []).push(arg);
      return of(opened() as DocumentDto);
    });
  }

  const createComponent = createComponentFactory({
    component: DocumentEditorPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: CatalogStore,
        useValue: { ensureLoaded: jest.fn(), $unitCodes: signal(new Map()) }
      },
      { provide: ProductsStore, useValue: {} },
      { provide: StockAvailabilityService, useValue: { bySearch: jest.fn(() => of(new Map())) } },
      {
        provide: DocumentsStore,
        useFactory: () => ({
          detail: {
            $data: opened,
            $isLoading: signal(false),
            $isFailed: signal(false),
            load: jest.fn(),
            reload: detailReload,
            reset: jest.fn()
          },
          counts: { load: jest.fn() },
          resetCommands,
          currentToken,
          ...commands
        })
      },
      { provide: PartnersStore, useValue: { loadActive: jest.fn(), $partners: signal([]) } },
      {
        provide: PermissionService,
        useValue: {
          has: (permission: string) => permission === PERMISSION.StockRead,
          $userId: signal(9)
        }
      },
      {
        provide: SystemSettingsStore,
        useValue: { $allowNegativeStock: signal(true), ensureLoaded: jest.fn() }
      },
      {
        provide: StockStore,
        useValue: {
          ledger: { $data: signal(undefined), $isLoading: signal(false), load: jest.fn() },
          summary: { load: jest.fn() }
        }
      },
      {
        provide: WarehousesStore,
        useValue: {
          ensureLoaded: jest.fn(),
          $warehouses: signal([{ id: 1, name: 'Main warehouse', isActive: true }]),
          $activeWarehouses: signal([{ id: 1, name: 'Main warehouse', isActive: true }])
        }
      }
    ]
  });

  async function open(doc: DocumentDto): Promise<void> {
    opened.set(doc);
    spectator = createComponent({
      props: { type: DocumentType.Issue, id: String(doc.id) } as never
    });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  const confirm = () => spectator.inject(ConfirmService);
  const text = (): string => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';

  beforeEach(() => {
    for (const key of Object.keys(calls)) {
      delete calls[key];
    }
    detailReload.mockClear();
    resetCommands.mockClear();
    currentToken.mockReset();
    commands = Object.fromEntries(
      ['save', 'submit', 'approve', 'reject', 'cancel', 'post', 'reverse'].map(name => [
        name,
        command(name)
      ])
    );
  });

  it('approving an override document asks for a reason and sends it with the version', async () => {
    await open(document({ requiresOverride: true }));

    spectator.component.onAction(DOCUMENT_ACTION.Approve);
    const request = confirm().$pending()?.request;

    expect(request?.heading).toBe('Approve ISS-WH01-2610-0001 with stock override?');
    expect(request?.reasonRequired).toBe(true);
    expect(request?.minReasonLength).toBe(5);

    confirm().confirm('Customer pre-paid');
    expect(calls['approve']).toEqual([
      { id: 2, concurrencyToken: 'AAAA', negativeStockOverrideReason: 'Customer pre-paid' }
    ]);
  });

  it('approving an ordinary document asks for no reason and sends none', async () => {
    await open(document());

    spectator.component.onAction(DOCUMENT_ACTION.Approve);

    expect(confirm().$pending()?.request.reasonLabel).toBeUndefined();
    confirm().confirm('');
    expect(calls['approve']).toEqual([
      { id: 2, concurrencyToken: 'AAAA', negativeStockOverrideReason: undefined }
    ]);
  });

  it('posting asks for no reason (it was given at approval) and sends the version only', async () => {
    await open(
      document({
        status: DocumentStatus.Approved,
        requiresOverride: true,
        negativeStockOverrideReason: 'Customer pre-paid',
        allowedActions: [DOCUMENT_ACTION.Post]
      })
    );

    spectator.component.onAction(DOCUMENT_ACTION.Post);

    expect(confirm().$pending()?.request.reasonLabel).toBeUndefined();
    confirm().confirm('');
    expect(calls['post']).toEqual([{ id: 2, concurrencyToken: 'AAAA' }]);
  });

  it('shows the flags and how short each line is, from the line itself (no stock query)', async () => {
    await open(document({ requiresOverride: true, isLarge: true }));

    expect(text()).toContain('Override');
    expect(text()).toContain('Large');
    expect(text()).toContain('Short by 5 (on hand 10)');
  });

  it('a stale document (409 without a product) shows the E4 banner; Reload rereads it', async () => {
    await open(document());
    expect(text()).not.toContain('changed since you opened it');

    commands['approve'].$operation.set(failureOperation(STALE));
    spectator.detectChanges();

    expect(text()).toContain('ISS-WH01-2610-0001 changed since you opened it');
    expect(text()).toContain('Your changes were not saved.');
    spectator.component.onReload();
    expect(resetCommands).toHaveBeenCalled();
    expect(detailReload).toHaveBeenCalled();
  });

  it('coming back to the tab finds a document someone else changed meanwhile, before any save', async () => {
    await open(document());
    currentToken.mockReturnValue(of('BBBB'));

    spectator.component.onVisibilityChange();
    spectator.detectChanges();

    expect(currentToken).toHaveBeenCalledWith(expect.objectContaining({ id: 2 }));
    expect(text()).toContain('changed since you opened it');
  });

  it('coming back to the tab with an unchanged document shows nothing', async () => {
    await open(document());
    currentToken.mockReturnValue(of('AAAA'));

    spectator.component.onVisibilityChange();
    spectator.detectChanges();

    expect(text()).not.toContain('changed since you opened it');
  });

  it('an insufficient-stock 409 is the E5 banner, not the stale one', async () => {
    await open(document());

    commands['post'].$operation.set(
      failureOperation({ ...STALE, extensions: { productId: 1, available: 2, requested: 15 } })
    );
    spectator.detectChanges();

    expect(text()).toContain('Stock changed');
    expect(text()).not.toContain('changed since you opened it');
  });

  it('a returned draft says who sent it back and why', async () => {
    await open(
      document({
        status: DocumentStatus.Draft,
        isReturned: true,
        rejectionReason: 'Please check the quantity first',
        returnedBy: 'supervisor',
        returnedByName: 'Supervisor',
        returnedAt: '2026-10-10T06:42:00Z',
        allowedActions: [DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit]
      })
    );

    expect(text()).toContain('Returned by Supervisor');
    expect(text()).toContain('Please check the quantity first');
  });
});
