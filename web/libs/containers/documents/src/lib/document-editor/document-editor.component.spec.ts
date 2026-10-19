import { computed, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { Subject, of, tap, throwError } from 'rxjs';
import {
  ApprovalMetric,
  ApproverTier,
  DocumentDto,
  DocumentStatus,
  DocumentType,
  IssuePurpose,
  ProductDto
} from '@ims/api';
import { provideUiText } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { PageTitleService, unsavedChangesGuard } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { SystemSettingsStore } from '@ims/core/settings';
import { CatalogStore, ProductsStore } from '@ims/features/catalog';
import { DocumentsStore, StockAvailabilityService } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PartnersStore, WarehousesStore } from '@ims/features/warehousing';
import {
  API_ERROR_KIND,
  ApiError,
  ApiOperation,
  DOCUMENT_ACTION,
  IDLE_OPERATION,
  PERMISSION,
  failureOperation,
  isFailure
} from '@ims/models';
import { ApiCommand } from '@ims/utils';
import { DocumentEditorPageComponent } from './document-editor.component';

/** A GUID for a small number, so the cases stay readable. */
const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;

function document(overrides: Partial<DocumentDto> = {}): DocumentDto {
  return {
    id: guid(2),
    type: DocumentType.Issue,
    no: 'ISS-WH01-2610-0001',
    status: DocumentStatus.Submitted,
    warehouseId: guid(1),
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
    createdById: guid(3),
    createdBy: 'staff',
    createdByName: 'Staff',
    createdAt: '2026-10-09T01:00:00Z',
    submittedById: guid(3),
    submittedBy: 'staff',
    submittedByName: 'Staff',
    submittedAt: '2026-10-09T02:00:00Z',
    approvedBy: undefined,
    approvedByName: undefined,
    approvedAt: undefined,
    postedBy: undefined,
    postedByName: undefined,
    postedAt: undefined,
    reversedBy: undefined,
    reversedByName: undefined,
    reversedAt: undefined,
    returnedById: undefined,
    returnedBy: undefined,
    returnedByName: undefined,
    returnedAt: undefined,
    totalQty: 15,
    totalAmount: 0,
    lines: [
      {
        productId: guid(1),
        sku: 'DEMO-001',
        productName: 'Demo widget',
        qty: 15,
        uomId: guid(1),
        uomCode: 'PCS',
        baseQty: 15,
        unitCost: 0,
        baseUnitCost: undefined,
        amount: undefined,
        costFromAverage: false,
        remark: undefined,
        available: 10
      }
    ],
    allowedActions: [DOCUMENT_ACTION.Approve, DOCUMENT_ACTION.Reject],
    concurrencyToken: 'AAAA',
    amountIsEstimate: false,
    approvalTriggers: [],
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
  const detailOperation = signal<ApiOperation>(IDLE_OPERATION);
  const lookup = jest.fn();
  const granted = new Set<string>([PERMISSION.StockRead]);
  /** What a command answers instead of the opened document (a save that creates, a submit that moves on). */
  const responses: Record<string, DocumentDto | undefined> = {};
  /** Commands that fail like a dead network. */
  const failing = new Map<string, unknown>();
  /** Commands that stay running until the test answers (to look at the page while a request is in flight). */
  const held = new Map<string, Subject<DocumentDto>>();
  let commands: Record<string, ApiCommand<never, DocumentDto>>;

  /** One recording command per action; like the real store, each answer becomes the opened document. */
  function command(name: string): ApiCommand<never, DocumentDto> {
    return new ApiCommand<never, DocumentDto>(arg => {
      (calls[name] ??= []).push(arg);
      if (failing.has(name)) {
        return throwError(() => failing.get(name));
      }
      const hold = held.get(name);
      if (hold) {
        return hold.pipe(tap(answer => opened.set(answer)));
      }
      const answer = responses[name] ?? (opened() as DocumentDto);
      opened.set(answer);
      return of(answer);
    });
  }

  const createComponent = createComponentFactory({
    component: DocumentEditorPageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      provideUiText(),
      {
        provide: CatalogStore,
        useValue: { ensureLoaded: jest.fn(), $unitCodes: signal(new Map()) }
      },
      { provide: ProductsStore, useValue: { lookup } },
      { provide: StockAvailabilityService, useValue: { bySearch: jest.fn(() => of(new Map())) } },
      {
        provide: DocumentsStore,
        useFactory: () => ({
          detail: {
            $data: opened,
            $isLoading: signal(false),
            $isFailed: computed(() => isFailure(detailOperation())),
            $operation: detailOperation,
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
          has: (permission: string) => granted.has(permission),
          $userId: signal(guid(9))
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
          $warehouses: signal([{ id: guid(1), name: 'Main warehouse', isActive: true }]),
          $activeWarehouses: signal([{ id: guid(1), name: 'Main warehouse', isActive: true }])
        }
      }
    ]
  });

  async function open(doc: DocumentDto, type = DocumentType.Issue): Promise<void> {
    opened.set(doc);
    spectator = createComponent({
      props: { type, id: doc.id } as never
    });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  async function openNew(type = DocumentType.Issue): Promise<void> {
    opened.set(undefined);
    spectator = createComponent({ props: { type, id: 'new' } as never });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  const WIDGET = {
    id: guid(1),
    sku: 'DEMO-001',
    name: 'Demo widget',
    baseUomId: guid(1),
    baseUomCode: 'PCS',
    conversions: [],
    isActive: true
  } as unknown as ProductDto;

  /** A complete new issue: Sale, one line of 1 PCS (the warehouse is preselected: there is only one). */
  function fillNewIssue(): void {
    const view = spectator.component.view;
    view.header.controls.purpose.setValue(IssuePurpose.Sale);
    view.header.markAsDirty();
    spectator.component.onProductPicked({ product: WIDGET, available: 10 });
    spectator.detectChanges();
  }

  const draftOf = (n: number): DocumentDto =>
    document({
      id: guid(n),
      status: DocumentStatus.Draft,
      requiresApproval: false,
      allowedActions: [DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit, DOCUMENT_ACTION.Cancel]
    });

  const confirm = () => spectator.inject(ConfirmService);
  const text = (): string => spectator.element.textContent?.replace(/\s+/g, ' ') ?? '';

  beforeEach(() => {
    for (const key of Object.keys(calls)) {
      delete calls[key];
    }
    detailReload.mockClear();
    resetCommands.mockClear();
    currentToken.mockReset();
    for (const key of Object.keys(responses)) {
      delete responses[key];
    }
    failing.clear();
    held.clear();
    detailOperation.set(IDLE_OPERATION);
    granted.clear();
    granted.add(PERMISSION.StockRead);
    commands = Object.fromEntries(
      ['save', 'submit', 'approve', 'reject', 'cancel', 'recall', 'post', 'reverse'].map(name => [
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
      { id: guid(2), concurrencyToken: 'AAAA', negativeStockOverrideReason: 'Customer pre-paid' }
    ]);
  });

  it('approving an ordinary document asks for no reason and sends none', async () => {
    await open(document());

    spectator.component.onAction(DOCUMENT_ACTION.Approve);

    expect(confirm().$pending()?.request.reasonLabel).toBeUndefined();
    confirm().confirm('');
    expect(calls['approve']).toEqual([
      { id: guid(2), concurrencyToken: 'AAAA', negativeStockOverrideReason: undefined }
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
    expect(calls['post']).toEqual([{ id: guid(2), concurrencyToken: 'AAAA' }]);
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

    expect(currentToken).toHaveBeenCalledWith(expect.objectContaining({ id: guid(2) }));
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
      failureOperation({
        ...STALE,
        extensions: { productId: guid(1), available: 2, requested: 15 }
      })
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

  describe('forms and dialogs (NV-09, NV-13, NV-18, NV-19, EA-06, EA-27, PS-17)', () => {
    it('EA-06: a failed submit puts the focus on the first field with an error', async () => {
      await openNew();
      spectator.component.onProductPicked({ product: WIDGET, available: 10 });
      spectator.detectChanges();

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      // Purpose is the first thing that is wrong: its group is marked, its first option has the focus.
      expect(spectator.query('fieldset[aria-invalid="true"]')).toBeTruthy();
      expect(window.document.activeElement?.getAttribute('type')).toBe('radio');
    });

    it('EA-06: with no line at all the focus goes to Add item, and the error is announced', async () => {
      await openNew();
      spectator.component.view.header.controls.purpose.setValue(IssuePurpose.Sale);

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(text()).toContain('Add at least one item.');
      expect(window.document.activeElement?.textContent).toContain('Add item');
    });

    it('phone QA 7: "Add at least one item." sits right under Add item, above the empty-state picture', async () => {
      await openNew();
      spectator.component.view.header.controls.purpose.setValue(IssuePurpose.Sale);

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      spectator.detectChanges();

      const message = spectator.query('.document-editor-lines > .document-editor-error');
      expect(message?.textContent).toContain('Add at least one item.');
      expect(message?.nextElementSibling?.tagName).toBe('APP-EMPTY-STATE');
    });

    it('NV-09: Purpose is marked required and the error says what to do', async () => {
      await openNew();
      spectator.component.view.header.controls.purpose.markAsTouched();
      spectator.detectChanges();

      // D4: a red asterisk and the word "required" for screen readers, not the text "(required)".
      expect(spectator.query('legend .ui-radio-group-asterisk')?.getAttribute('aria-hidden')).toBe(
        'true'
      );
      expect(spectator.query('legend .ui-radio-group-sr')?.textContent).toBe('required');
      expect(spectator.query('legend')?.textContent).not.toContain('(required)');
      expect(spectator.queryAll('app-form-legend')).toHaveLength(1);
      expect(spectator.query('#document-purpose-error')?.textContent).toContain(
        'Choose a purpose.'
      );
    });

    it('NV-09: a quantity of -5 says what is allowed', async () => {
      await openNew();
      fillNewIssue();
      const quantity = spectator.component.view.lines.at(0).controls.qty;

      quantity.setValue(-5);
      quantity.markAsTouched();
      spectator.detectChanges();

      expect(text()).toContain('Enter a quantity above 0.');
    });

    it('ER-12: 0.0001 is accepted, 0.00001 says "Use at most 4 decimals." and is left out of the totals line', async () => {
      await openNew();
      fillNewIssue();
      const quantity = spectator.component.view.lines.at(0).controls.qty;

      quantity.setValue(0.0001);
      quantity.markAsTouched();
      spectator.detectChanges();
      expect(text()).not.toContain('Enter a quantity above 0.');
      expect(text()).toContain('1 line | 0.0001 units');

      quantity.setValue(0.00001);
      spectator.detectChanges();
      expect(text()).toContain('Use at most 4 decimals.');
      expect(text()).not.toContain('Enter a quantity above 0.');
      expect(text()).toContain('0 lines | 0 units');
    });

    it('EA-27: every control of a line names its product', async () => {
      await openNew();
      fillNewIssue();

      const labels = spectator
        .queryAll('app-document-line-editor button, app-document-line-editor input')
        .map(element => element.getAttribute('aria-label'));

      expect(labels).toEqual(
        expect.arrayContaining([
          'Decrease quantity of DEMO-001',
          'Quantity of DEMO-001',
          'Increase quantity of DEMO-001',
          'Remove DEMO-001'
        ])
      );
    });

    it('NV-18: removing a line with a typed quantity asks first; Keep leaves it', async () => {
      await openNew();
      fillNewIssue();
      spectator.component.view.lines.at(0).controls.qty.setValue(7);
      spectator.detectChanges();

      spectator.component.onRemoveLine(0);
      expect(confirm().$pending()?.request.heading).toBe('Remove DEMO-001?');
      confirm().cancel();

      expect(spectator.component.view.lines.length).toBe(1);

      spectator.component.onRemoveLine(0);
      confirm().confirm('');
      expect(spectator.component.view.lines.length).toBe(0);
    });

    it('NR-09: the remove dialog says "cost" only on a receipt and only with cost.view', async () => {
      const removeBody = (): string => {
        spectator.component.onRemoveLine(0);
        const body = confirm().$pending()?.request.body ?? '';
        confirm().cancel();
        return body;
      };
      await openNew(DocumentType.Receipt);
      spectator.component.onProductPicked({ product: WIDGET, available: 10 });
      spectator.component.view.lines.at(0).controls.qty.setValue(7);

      const staff = removeBody();
      granted.add(PERMISSION.CostView);
      const clerk = removeBody();

      await openNew();
      spectator.component.onProductPicked({ product: WIDGET, available: 10 });
      spectator.component.view.lines.at(0).controls.qty.setValue(7);
      const issue = removeBody();

      expect(staff).toBe('Its quantity will be lost.');
      expect(clerk).toBe('Its quantity and cost will be lost.');
      expect(issue).toBe('Its quantity will be lost.');
    });

    it('NV-18: a line nobody changed goes at once', async () => {
      await openNew();
      fillNewIssue();

      spectator.component.onRemoveLine(0);

      expect(confirm().$pending()).toBeUndefined();
      expect(spectator.component.view.lines.length).toBe(0);
    });

    it('NV-19: the Post dialog names the products and quantities, not only the line count', async () => {
      await open(
        document({
          status: DocumentStatus.Submitted,
          requiresApproval: false,
          allowedActions: [DOCUMENT_ACTION.Post]
        })
      );

      spectator.component.onAction(DOCUMENT_ACTION.Post);

      expect(confirm().$pending()?.request.body).toContain('Includes DEMO-001 x 15 PCS.');
    });

    it('PS-17: approving an override says where each short line will end up', async () => {
      await open(document({ requiresOverride: true }));

      spectator.component.onAction(DOCUMENT_ACTION.Approve);

      expect(confirm().$pending()?.request.body).toContain('DEMO-001 will go to -5 PCS.');
    });

    it('PS-17: the read-only view says who submitted the document and when', async () => {
      await open(document());

      expect(text()).toContain('Submitted by Staff');
    });

    it('NV-21: no button keeps a spinner once the command has finished', async () => {
      await openNew();
      fillNewIssue();
      responses['save'] = draftOf(31);
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.component.onAction(DOCUMENT_ACTION.Edit);

      expect(spectator.component.$busyAction()).toBeUndefined();
    });
  });

  describe('a link that starts a receipt for a product (NV-13)', () => {
    // Two warehouses, so nothing is preselected and the link is the only thing that can choose one.
    const LETTERS_ID = 'abcdef01-2345-4678-89ab-cdef01234567';
    const twoWarehouses = {
      provide: WarehousesStore,
      useValue: {
        ensureLoaded: jest.fn(),
        $warehouses: signal([
          { id: guid(1), name: 'Main warehouse', isActive: true },
          { id: LETTERS_ID, name: 'Second warehouse', isActive: true }
        ]),
        $activeWarehouses: signal([
          { id: guid(1), name: 'Main warehouse', isActive: true },
          { id: LETTERS_ID, name: 'Second warehouse', isActive: true }
        ])
      }
    };

    it('picks the warehouse and the product and focuses the quantity', async () => {
      lookup.mockReturnValue(of({ found: true, product: WIDGET }));
      opened.set(undefined);
      spectator = createComponent({
        props: {
          type: DocumentType.Receipt,
          id: 'new',
          sku: 'DEMO-001',
          warehouseId: guid(1)
        } as never
      });
      spectator.detectChanges();
      await spectator.fixture.whenStable();
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(lookup).toHaveBeenCalledWith('DEMO-001');
      expect(spectator.component.$warehouseId()).toBe(guid(1));
      expect(spectator.component.view.lines.length).toBe(1);
      expect(window.document.activeElement?.closest('app-document-line-editor')).toBeTruthy();
    });

    it('ignores a link from before the id change (a number is not a warehouse id)', async () => {
      lookup.mockReturnValue(of({ found: false, product: undefined }));
      opened.set(undefined);
      spectator = createComponent({
        providers: [twoWarehouses],
        props: { type: DocumentType.Receipt, id: 'new', sku: 'DEMO-001', warehouseId: '1' } as never
      });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(spectator.component.$warehouseId()).toBeNull();
    });

    it('lower-cases a hand-typed warehouse id so the select shows it', async () => {
      lookup.mockReturnValue(of({ found: false, product: undefined }));
      opened.set(undefined);
      spectator = createComponent({
        providers: [twoWarehouses],
        props: {
          type: DocumentType.Receipt,
          id: 'new',
          sku: 'DEMO-001',
          warehouseId: LETTERS_ID.toUpperCase()
        } as never
      });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(spectator.component.$warehouseId()).toBe(LETTERS_ID);
    });

    it('an unknown or deactivated product leaves the form empty', async () => {
      lookup.mockReturnValue(of({ found: false, product: undefined }));
      opened.set(undefined);
      spectator = createComponent({
        props: { type: DocumentType.Receipt, id: 'new', sku: 'NOPE' } as never
      });
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(spectator.component.view.lines.length).toBe(0);
    });
  });

  describe('a document that cannot be opened (NV-12, PS-20)', () => {
    it('an unknown id is a not-found page with a way home, not a retry button', async () => {
      opened.set(undefined);
      detailOperation.set(
        failureOperation({ ...STALE, kind: API_ERROR_KIND.NotFound, status: 404, detail: '' })
      );
      spectator = createComponent({
        props: { type: DocumentType.Receipt, id: guid(99999) } as never
      });
      spectator.detectChanges();

      expect(text()).toContain("This page or document doesn't exist or was removed.");
      expect(spectator.query('h1')?.textContent).toContain('Page not found');
      expect(spectator.query('a[href="/"]')?.textContent).toContain('Go to Home');
      expect(text()).not.toContain('Retry');
    });

    it.each([DocumentType.Receipt, DocumentType.Issue])(
      'an old numeric bookmark (%s) is a not-found page and makes no request',
      async type => {
        opened.set(undefined);
        spectator = createComponent({ props: { type, id: '123' } as never });
        spectator.detectChanges();
        await spectator.fixture.whenStable();
        spectator.detectChanges();

        const detail = spectator.inject(DocumentsStore).detail;
        expect(detail.load).not.toHaveBeenCalled();
        expect(spectator.component.$isNew()).toBe(false);
        expect(spectator.query('h1')?.textContent).toContain('Page not found');
        expect(text()).toContain("This page or document doesn't exist or was removed.");
        expect(text()).not.toContain('Retry');
      }
    );

    it('a failed load is one alert with the mapped sentence and a Retry, and no toast says it again (R11)', async () => {
      opened.set(undefined);
      detailOperation.set(
        failureOperation({
          ...STALE,
          kind: API_ERROR_KIND.Network,
          status: 0,
          detail: '',
          message: "Can't reach the server. Check your connection and try again."
        })
      );
      jest.clearAllMocks();
      spectator = createComponent({ props: { type: DocumentType.Receipt, id: guid(5) } as never });
      spectator.detectChanges();

      const alerts = spectator.queryAll('[role="alert"]');
      expect(alerts).toHaveLength(1);
      expect(alerts[0].textContent).toContain("Can't reach the server.");
      expect(alerts[0].textContent).toContain('Retry');
      // A saved document asks for its form support data once it opened, so no sibling call adds a toast.
      expect(spectator.inject(PartnersStore).loadActive).not.toHaveBeenCalled();
    });

    it('while a saved document loads the title is not "New receipt"', async () => {
      opened.set(undefined);
      spectator = createComponent({ props: { type: DocumentType.Receipt, id: guid(5) } as never });
      spectator.detectChanges();

      expect(spectator.query('h1')?.textContent).not.toContain('New receipt');
    });
  });

  describe('a new document (regression NV-01, NV-02)', () => {
    it('NV-01: after the first Save the form is clean when the URL moves, so no "Discard changes?" and no duplicate', async () => {
      await openNew();
      fillNewIssue();
      expect(spectator.component.hasUnsavedChanges()).toBe(true);
      responses['save'] = draftOf(31);
      const dirtyWhenNavigating: boolean[] = [];
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate').mockImplementation(() => {
        dirtyWhenNavigating.push(spectator.component.hasUnsavedChanges());
        return Promise.resolve(true);
      });

      spectator.component.onAction(DOCUMENT_ACTION.Edit);
      spectator.detectChanges();

      expect(navigate).toHaveBeenCalledWith(['/', 'issues', guid(31)], { replaceUrl: true });
      expect(dirtyWhenNavigating).toEqual([false]);
      // The real guard of the route lets the move through without asking.
      const guardAnswer = TestBed.runInInjectionContext(() =>
        unsavedChangesGuard(spectator.component, {} as never, {} as never, {} as never)
      );
      expect(guardAnswer).toBe(true);
      expect(confirm().$pending()).toBeUndefined();
    });

    it('NV-01: a second tap on Save draft updates the saved document instead of creating another one', async () => {
      await openNew();
      fillNewIssue();
      responses['save'] = draftOf(31);
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.component.onAction(DOCUMENT_ACTION.Edit);
      spectator.detectChanges();
      spectator.component.onAction(DOCUMENT_ACTION.Edit);

      expect(calls['save']).toHaveLength(2);
      expect(calls['save'][0]).toEqual(expect.objectContaining({ ref: undefined }));
      expect(calls['save'][1]).toEqual(
        expect.objectContaining({ ref: { id: guid(31), concurrencyToken: 'AAAA' } })
      );
    });

    it('NV-02: Back in the Submit & post dialog sends nothing, so nothing is created twice or at all', async () => {
      granted.add(PERMISSION.DocumentPost);
      await openNew();
      fillNewIssue();
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      expect(confirm().$pending()?.request.heading).toBe('Submit & post?');
      confirm().cancel();
      spectator.detectChanges();

      expect(calls['save']).toBeUndefined();
      expect(calls['submit']).toBeUndefined();
      expect(spectator.component.$isNew()).toBe(true);
    });

    it('a save that fails before the document exists leaves the editor new', async () => {
      await openNew();
      fillNewIssue();
      failing.set('save', new Error('offline'));
      const navigate = jest.spyOn(spectator.inject(Router), 'navigate');

      expect(() => spectator.component.onAction(DOCUMENT_ACTION.Edit)).not.toThrow();

      expect(navigate).not.toHaveBeenCalled();
      expect(spectator.component.$isNew()).toBe(true);
    });

    it('NV-07: closing the tab with a half-written document asks first; a clean one does not', async () => {
      await openNew();
      const event = new Event('beforeunload', { cancelable: true }) as BeforeUnloadEvent;
      spectator.component.onBeforeUnload(event);
      expect(event.defaultPrevented).toBe(false);

      fillNewIssue();
      spectator.component.onBeforeUnload(event);
      expect(event.defaultPrevented).toBe(true);
    });

    it('the screen title names the new document, then the saved one (EA-03)', async () => {
      await openNew();
      expect(spectator.inject(PageTitleService).$title()).toContain('New issue');

      await open(document());
      expect(spectator.inject(PageTitleService).$title()).toContain('ISS-WH01-2610-0001');
    });
  });

  describe('adding the same product twice (NV-04)', () => {
    it('the message says the line grew and what the quantity is now', async () => {
      await openNew();

      spectator.component.onProductPicked({ product: WIDGET, available: 10 });
      expect(spectator.component.$lastAdded()).toBe('DEMO-001 added.');
      spectator.component.onProductPicked({ product: WIDGET, available: 10 });
      expect(spectator.component.$lastAdded()).toBe('DEMO-001: now 2.');
    });

    it('typing something else in the search clears the message', async () => {
      await openNew();
      spectator.component.onProductPicked({ product: WIDGET, available: 10 });

      spectator.component.onPickerTermChanged();

      expect(spectator.component.$lastAdded()).toBeUndefined();
    });
  });

  describe('what a worker can do with a document (NV-05, NV-06, NV-17)', () => {
    it('NV-05: a posted document tells a worker without the Reverse right whom to ask', async () => {
      granted.add(PERMISSION.DocumentCreate);
      await open(document({ status: DocumentStatus.Posted, allowedActions: [] }));

      expect(text()).toContain('Ask a supervisor to reverse this document');
      expect(spectator.query('.document-editor-bar')).toBeNull();
    });

    it('NV-05: a supervisor sees the Reverse button and no such note', async () => {
      granted.add(PERMISSION.DocumentCreate);
      await open(
        document({ status: DocumentStatus.Posted, allowedActions: [DOCUMENT_ACTION.Reverse] })
      );

      expect(text()).not.toContain('Ask a supervisor');
      expect(text()).toContain('Reverse');
    });

    it('NV-06: a submitted issue that cannot be posted says to cancel and redo it, not to adjust the quantity', async () => {
      await open(
        document({
          status: DocumentStatus.Submitted,
          requiresApproval: false,
          allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel]
        })
      );

      commands['post'].$operation.set(
        failureOperation({
          ...STALE,
          extensions: { productId: guid(1), available: 0, requested: 24 }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain('Cancel this document and create a new one with a smaller quantity');
      expect(text()).not.toContain('Adjust the quantity');
    });

    it('NV-06: without the right to cancel it says to ask a supervisor', async () => {
      await open(document({ allowedActions: [DOCUMENT_ACTION.Post] }));

      commands['post'].$operation.set(
        failureOperation({
          ...STALE,
          extensions: { productId: guid(1), available: 0, requested: 24 }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain('Ask a supervisor for help');
    });

    it('NV-06: a draft can still be adjusted, so it keeps the original advice', async () => {
      await open(draftOf(2));

      commands['submit'].$operation.set(
        failureOperation({
          ...STALE,
          extensions: { productId: guid(1), available: 0, requested: 24 }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain('Adjust the quantity and try again');
    });

    it('PS-01: a blocked reversal names every product that would go negative, and is not the stale banner', async () => {
      await open(
        document({ status: DocumentStatus.Posted, allowedActions: [DOCUMENT_ACTION.Reverse] })
      );

      commands['reverse'].$operation.set(
        failureOperation({
          ...STALE,
          detail: "Can't reverse ISS-WH01-2610-0001. It would make 2 products negative.",
          extensions: {
            code: 'reversal_blocked',
            shortages: [
              { productId: guid(1), sku: 'DEMO-001', uomCode: 'PCS', balance: -3 },
              { productId: guid(2), sku: 'QA-002', uomCode: 'PCS', balance: -6 }
            ]
          }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain("Can't reverse ISS-WH01-2610-0001");
      expect(text()).toContain('DEMO-001 (-3 PCS), QA-002 (-6 PCS) negative');
      expect(text()).not.toContain('changed since you opened it');
      expect(text()).not.toContain('Adjust the quantity');
    });

    it('EA-07: after a refused reversal the focus is on the error, not left on the page body', async () => {
      await open(
        document({ status: DocumentStatus.Posted, allowedActions: [DOCUMENT_ACTION.Reverse] })
      );
      failing.set('reverse', {
        ...STALE,
        extensions: {
          code: 'reversal_blocked',
          shortages: [{ productId: guid(1), sku: 'DEMO-001', uomCode: 'PCS', balance: -1 }]
        }
      });
      (window.document.activeElement as HTMLElement | null)?.blur();

      spectator.component.onAction(DOCUMENT_ACTION.Reverse);
      confirm().confirm('Wrong supplier note');
      spectator.detectChanges();
      await spectator.fixture.whenStable();

      expect(window.document.activeElement?.tagName).toBe('APP-BANNER');
    });

    it('E4: the stale banner names who changed the document and when, when the API says so', async () => {
      await open(document());

      commands['approve'].$operation.set(
        failureOperation({
          ...STALE,
          extensions: {
            code: 'document_changed',
            changedBy: 'Raj',
            changedAt: '2026-10-10T06:42:00Z'
          }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain('Raj changed this document at 10/10/2026');
    });

    it('PS-01 (older API): one product named through the insufficient-stock members still reads as a blocked reversal', async () => {
      await open(
        document({ status: DocumentStatus.Posted, allowedActions: [DOCUMENT_ACTION.Reverse] })
      );

      commands['reverse'].$operation.set(
        failureOperation({
          ...STALE,
          extensions: { productId: guid(1), available: -3, requested: 5 }
        })
      );
      spectator.detectChanges();

      expect(text()).toContain("Can't reverse ISS-WH01-2610-0001");
      expect(text()).toContain('DEMO-001 (-8 PCS) negative');
      expect(text()).not.toContain('Adjust the quantity');
    });

    it('NV-17: the header says who approved or posted and when', async () => {
      await open(
        document({
          status: DocumentStatus.Posted,
          postedByName: 'Supervisor',
          postedAt: '2026-10-09T08:00:00Z',
          allowedActions: []
        })
      );

      expect(text()).toContain('Posted by Supervisor on 09/10/2026');
    });

    it('PS-03: the approver sees why approval is needed and the estimated value', async () => {
      granted.add(PERMISSION.CostView);
      await open(
        document({
          totalAmount: 5100,
          amountIsEstimate: true,
          approvalTriggers: [
            {
              metric: ApprovalMetric.Amount,
              threshold: 5000,
              actual: 5100,
              approverTier: ApproverTier.Standard,
              roleRequired: 'Supervisor'
            }
          ]
        })
      );

      expect(text()).toContain('Why this needs approval');
      expect(text()).toContain(
        'The value 5,100.00 reaches the 5,000.00 limit. Approver: Supervisor.'
      );
      expect(text()).toContain('5,100.00 est.');
    });

    it('PS-03: a quantity rule reads as a quantity', async () => {
      await open(
        document({
          approvalTriggers: [
            {
              metric: ApprovalMetric.Quantity,
              threshold: 1000,
              actual: 1000,
              approverTier: ApproverTier.Large,
              roleRequired: 'Owner'
            }
          ]
        })
      );

      expect(text()).toContain('The quantity 1,000 reaches the 1,000 limit. Approver: Owner.');
    });

    it('NV-17: a submitted document that needs no approval says it is ready to post', async () => {
      await open(document({ requiresApproval: false, allowedActions: [DOCUMENT_ACTION.Post] }));

      expect(text()).toContain('Ready to post');
    });
  });

  describe('cost visibility (cost.view, docs/design/mvp-ux.md 1.4)', () => {
    const STAFF: string[] = [PERMISSION.StockRead, PERMISSION.DocumentCreate];
    const CLERK: string[] = [...STAFF, PERMISSION.CostView];
    const SUPERVISOR: string[] = [...CLERK, PERMISSION.ValuationView, PERMISSION.DocumentApprove];

    const costInput = (): Element | null =>
      spectator.query('input[aria-label^="Unit cost of"]') ?? null;

    async function openNewReceipt(...permissions: string[]): Promise<void> {
      permissions.forEach(permission => granted.add(permission));
      opened.set(undefined);
      spectator = createComponent({ props: { type: DocumentType.Receipt, id: 'new' } as never });
      spectator.detectChanges();
      await spectator.fixture.whenStable();
      spectator.component.onProductPicked({ product: WIDGET, available: 0 });
      spectator.detectChanges();
    }

    /** A posted receipt as the API answers it: the cost fields are null for a caller without cost.view. */
    const postedReceipt = (withCost: boolean): DocumentDto =>
      document({
        type: DocumentType.Receipt,
        no: 'RCP-WH01-2610-0001',
        status: DocumentStatus.Posted,
        allowedActions: [],
        requiresApproval: false,
        totalAmount: withCost ? 125.5 : (null as unknown as undefined),
        lines: [
          {
            productId: guid(1),
            sku: 'DEMO-001',
            productName: 'Demo widget',
            qty: 10,
            uomId: guid(1),
            uomCode: 'PCS',
            baseQty: 10,
            unitCost: withCost ? 12.55 : (null as unknown as undefined),
            baseUnitCost: undefined,
            amount: undefined,
            costFromAverage: false,
            remark: undefined,
            available: undefined
          }
        ]
      });

    it('Warehouse staff: a new receipt has quantity but no unit cost input and no amount', async () => {
      await openNewReceipt(...STAFF);
      const qty = spectator.component.view.lines.at(0).controls.qty;
      qty.setValue(4);
      spectator.detectChanges();

      expect(spectator.queryAll('app-document-line-editor input')).toHaveLength(1);
      expect(costInput()).toBeNull();
      expect(text()).not.toContain('Unit cost');
      expect(text()).not.toMatch(/\d\.\d{2}/);
    });

    it('Clerk: a new receipt has the unit cost input and the total grows with it', async () => {
      await openNewReceipt(...CLERK);
      const line = spectator.component.view.lines.at(0);
      line.controls.qty.setValue(4);
      line.controls.unitCost.setValue(2.5);
      spectator.detectChanges();

      expect(costInput()).not.toBeNull();
      expect(text()).toContain('Unit cost');
      expect(text()).toContain('10.00');
    });

    it('R04: a unit cost with a 3rd decimal is blocked with a message, not rounded', async () => {
      await openNewReceipt(...CLERK);
      const line = spectator.component.view.lines.at(0);
      line.controls.qty.setValue(4);

      line.controls.unitCost.setValue(12.345);
      line.controls.unitCost.markAsTouched();
      spectator.detectChanges();

      expect(text()).toContain('Enter an amount with at most 2 decimals.');
      expect(line.controls.unitCost.value).toBe(12.345);
      expect(spectator.component.view.lines.invalid).toBe(true);
      // The amount is left out until the cost is valid.
      expect(text()).not.toContain('49.38');

      for (const accepted of [10, 10.5, 10.55]) {
        line.controls.unitCost.setValue(accepted);
        expect(line.controls.unitCost.valid).toBe(true);
      }
    });

    it('R04: pasted text the browser cannot read ("1,234.50") is an error that blocks Save, not an empty cost', async () => {
      await openNewReceipt(...CLERK);
      const line = spectator.component.view.lines.at(0);
      line.controls.qty.setValue(4);
      spectator.detectChanges();
      const input = costInput() as HTMLInputElement;
      Object.defineProperty(input, 'validity', { configurable: true, value: { badInput: true } });

      input.dispatchEvent(new Event('input'));
      line.controls.unitCost.markAsTouched();
      spectator.detectChanges();

      expect(line.controls.unitCost.errors).toEqual({ badInput: true });
      expect(spectator.component.view.isValid()).toBe(false);
      expect(text()).toContain('Enter a number such as 1234.50, without commas or spaces.');
    });

    it('R04: a negative unit cost is refused and an empty one is allowed', async () => {
      await openNewReceipt(...CLERK);
      const cost = spectator.component.view.lines.at(0).controls.unitCost;

      cost.setValue(-1);
      expect(cost.errors).toHaveProperty('min');
      cost.setValue(null);
      expect(cost.valid).toBe(true);
    });

    it('Warehouse staff: what is sent for a saved receipt line carries no unit cost', async () => {
      await openNewReceipt(...STAFF);
      spectator.component.view.lines.at(0).controls.qty.setValue(4);
      spectator.component.view.header.markAsDirty();
      spectator.detectChanges();

      const request = spectator.component.view.toRequest();
      expect(request.lines).toHaveLength(1);
      expect(request.lines[0].qty).toBe(4);
      expect(request.lines[0].unitCost).toBeUndefined();
    });

    it('Warehouse staff: a posted receipt shows no amount, no unit cost and no placeholder', async () => {
      STAFF.forEach(permission => granted.add(permission));
      await open(postedReceipt(false), DocumentType.Receipt);

      expect(text()).toContain('DEMO-001');
      expect(text()).not.toContain('Amount');
      expect(text()).not.toMatch(/\d\.\d{2}/);
      expect(text()).not.toContain('· -');
    });

    it('Clerk and Supervisor: a posted receipt shows the amount and the unit cost', async () => {
      SUPERVISOR.forEach(permission => granted.add(permission));
      await open(postedReceipt(true), DocumentType.Receipt);

      expect(text()).toContain('Amount');
      expect(text()).toContain('125.50');
      expect(text()).toContain('12.55');
    });

    it('Warehouse staff: "why approval is needed" names the value rule and the approver, with no number', async () => {
      STAFF.forEach(permission => granted.add(permission));
      await open(
        document({
          totalAmount: null as unknown as undefined,
          approvalTriggers: [
            {
              metric: ApprovalMetric.Amount,
              threshold: null as unknown as undefined,
              actual: null as unknown as undefined,
              approverTier: ApproverTier.Standard,
              roleRequired: 'Supervisor'
            }
          ]
        })
      );

      expect(text()).toContain('Why this needs approval');
      expect(text()).toContain('Its value reaches an approval limit. Approver: Supervisor.');
      expect(text()).not.toContain('undefined');
      expect(text()).not.toMatch(/\d\.\d{2}/);
    });

    it('Warehouse staff still see the numbers of a quantity rule', async () => {
      STAFF.forEach(permission => granted.add(permission));
      await open(
        document({
          totalAmount: null as unknown as undefined,
          approvalTriggers: [
            {
              metric: ApprovalMetric.Quantity,
              threshold: 1000,
              actual: 1200,
              approverTier: ApproverTier.Large,
              roleRequired: 'Owner'
            }
          ]
        })
      );

      expect(text()).toContain('The quantity 1,200 reaches the 1,000 limit. Approver: Owner.');
    });
  });

  describe('phone QA 2: Submit & post is one clear sequence', () => {
    const toasts = (): string[] =>
      TestBed.inject(ToastService)
        .$toasts()
        .map(toast => toast.message);
    const submittedNoApproval = (): DocumentDto =>
      document({
        id: guid(31),
        requiresApproval: false,
        allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]
      });
    const submittedForApproval = (): DocumentDto =>
      document({
        id: guid(31),
        requiresApproval: true,
        allowedActions: [DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]
      });
    const posted = (): DocumentDto =>
      document({
        id: guid(31),
        status: DocumentStatus.Posted,
        requiresApproval: false,
        allowedActions: []
      });
    const bar = (): string =>
      (spectator.query('.document-editor-bar')?.textContent ?? '').replace(/\s+/g, ' ');

    async function startNewIssue(): Promise<void> {
      await openNew();
      fillNewIssue();
      responses['save'] = draftOf(31);
      jest.spyOn(spectator.inject(Router), 'navigate').mockResolvedValue(true);
    }

    it('whoever may post is asked ONCE, before anything is sent, and no dialog follows the submit', async () => {
      granted.add(PERMISSION.DocumentPost);
      await startNewIssue();
      responses['submit'] = submittedNoApproval();
      responses['post'] = posted();

      spectator.component.onAction(DOCUMENT_ACTION.Submit);

      const request = confirm().$pending()?.request;
      expect(calls['save']).toBeUndefined();
      expect(request?.heading).toBe('Submit & post?');
      expect(request?.confirmLabel).toBe('Submit & post');
      expect(request?.body).toContain('Stock will decrease for 1 line.');
      expect(request?.body).toContain('Includes DEMO-001 x 1 PCS.');
      expect(request?.body).toContain('If it needs approval, it goes to a supervisor first');

      confirm().confirm('');
      spectator.detectChanges();

      expect(confirm().$pending()).toBeUndefined();
      expect(calls['save']).toHaveLength(1);
      expect(calls['submit']).toEqual([{ id: guid(31), concurrencyToken: 'AAAA' }]);
      expect(calls['post']).toEqual([{ id: guid(31), concurrencyToken: 'AAAA' }]);
      expect(toasts()).toEqual(['Posted. Stock updated.']);
    });

    it('while the post runs the person sees one busy state, "Posting...", and no "Ready to post"', async () => {
      granted.add(PERMISSION.DocumentPost);
      await startNewIssue();
      responses['submit'] = submittedNoApproval();
      held.set('post', new Subject<DocumentDto>());

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      confirm().confirm('');
      spectator.detectChanges();

      expect(spectator.component.$isBusy()).toBe(true);
      expect(bar()).toContain('Posting...');
      expect(bar()).not.toContain('Submit & post');
      expect(text()).not.toContain('Ready to post');
      expect(text()).not.toContain('Wrong quantity? Tap Edit.');

      held.get('post')?.next(posted());
      held.get('post')?.complete();
      spectator.detectChanges();

      expect(spectator.component.$isBusy()).toBe(false);
      expect(text()).toContain('Posted');
    });

    it('while the submit runs the button says "Submitting..."', async () => {
      granted.add(PERMISSION.DocumentPost);
      await startNewIssue();
      held.set('submit', new Subject<DocumentDto>());

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      confirm().confirm('');
      spectator.detectChanges();

      expect(bar()).toContain('Submitting...');
    });

    it('a document that needs approval stops at Submitted and the toast says so; Post is never called', async () => {
      granted.add(PERMISSION.DocumentPost);
      await startNewIssue();
      responses['submit'] = submittedForApproval();

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      confirm().confirm('');
      spectator.detectChanges();

      expect(calls['post']).toBeUndefined();
      expect(confirm().$pending()).toBeUndefined();
      expect(toasts()).toEqual(['Submitted for approval.']);
      expect(text()).toContain('Awaiting approval');
    });

    it('someone who cannot post is not asked, and is told who it waits for; "Ready to post" never shows', async () => {
      await startNewIssue();
      responses['submit'] = document({
        id: guid(31),
        requiresApproval: false,
        allowedActions: [DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]
      });

      spectator.component.onAction(DOCUMENT_ACTION.Submit);
      spectator.detectChanges();

      expect(confirm().$pending()).toBeUndefined();
      expect(calls['post']).toBeUndefined();
      expect(toasts()).toEqual(['Submitted. Waiting for a warehouse user to post.']);
      expect(text()).toContain('Waiting for a warehouse user to post.');
      expect(text()).not.toContain('Ready to post');
    });

    it('opening a Submitted document the person cannot post says who it waits for, not "Ready to post"', async () => {
      await open(
        document({
          requiresApproval: false,
          allowedActions: [DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]
        })
      );

      expect(text()).toContain('Waiting for a warehouse user to post.');
      expect(text()).not.toContain('Ready to post');
    });
  });

  describe('correcting a document before it posts: recall and cancel on Approved (R28, R31)', () => {
    const toasts = (): string[] =>
      TestBed.inject(ToastService)
        .$toasts()
        .map(toast => toast.message);
    const submitted = (overrides: Partial<DocumentDto> = {}): DocumentDto =>
      document({
        requiresApproval: false,
        allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel],
        ...overrides
      });
    const recalledDraft = (): DocumentDto =>
      document({
        status: DocumentStatus.Draft,
        requiresApproval: false,
        concurrencyToken: 'BBBB',
        allowedActions: [DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit, DOCUMENT_ACTION.Cancel]
      });
    /** The main buttons of the bar (Cancel document and the "more" button are the same on every row of the table). */
    const labels = (): string[] =>
      spectator
        .queryAll('.document-editor-bar button')
        .map(button => (button.textContent ?? '').trim())
        .filter(label => label !== 'Cancel document' && label !== '');

    it('Edit shows only when allowedActions lists recall, secondary next to Post', async () => {
      await open(submitted());

      expect(labels()).toEqual(['Edit', 'Post']);
      expect(text()).toContain('Wrong quantity? Tap Edit.');

      await open(submitted({ allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel] }));
      expect(labels()).not.toContain('Edit');
      expect(text()).not.toContain('Tap Edit');
    });

    it('a document waiting for approval: Edit is the primary button of the bar', async () => {
      await open(document({ allowedActions: [DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel] }));

      const edit = spectator
        .queryAll('.document-editor-bar button')
        .find(button => button.textContent?.includes('Edit'));
      expect(edit?.closest('app-button')?.className).toContain('--primary');
      expect(text()).toContain('Need to change something? Tap Edit.');
    });

    it('phone QA 3: an approval-free document asks first too; Back sends nothing, then it opens as an editable Draft with one toast', async () => {
      await open(submitted());
      responses['recall'] = recalledDraft();

      spectator.component.onAction(DOCUMENT_ACTION.Recall);
      const request = confirm().$pending()?.request;
      expect(request?.heading).toBe('Take ISS-WH01-2610-0001 back to draft?');
      expect(request?.confirmLabel).toBe('Back to draft');
      expect(request?.cancelLabel).toBe('Back');
      expect(request?.body).not.toContain('approval');

      confirm().cancel();
      expect(calls['recall']).toBeUndefined();
      expect(toasts()).toEqual([]);

      spectator.component.onAction(DOCUMENT_ACTION.Recall);
      confirm().confirm('');
      spectator.detectChanges();
      await spectator.fixture.whenStable();
      spectator.detectChanges();

      expect(confirm().$pending()).toBeUndefined();
      expect(calls['recall']).toEqual([{ id: guid(2), concurrencyToken: 'AAAA' }]);
      expect(toasts()).toEqual(['Back to draft. Fix it, then submit again.']);
      expect(spectator.component.$editable()).toBe(true);
      expect(labels().some(label => label.includes('Save draft'))).toBe(true);
      expect(text()).not.toContain('Returned');
    });

    it('a document waiting for approval asks first, and Keep waiting sends nothing', async () => {
      await open(document({ allowedActions: [DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel] }));

      spectator.component.onAction(DOCUMENT_ACTION.Recall);
      const request = confirm().$pending()?.request;
      expect(request?.heading).toBe('Take ISS-WH01-2610-0001 back to draft?');
      expect(request?.body).toContain('leaves the approval queue');
      expect(request?.cancelLabel).toBe('Keep waiting');

      confirm().cancel();
      expect(calls['recall']).toBeUndefined();

      spectator.component.onAction(DOCUMENT_ACTION.Recall);
      responses['recall'] = recalledDraft();
      confirm().confirm('');
      expect(calls['recall']).toEqual([{ id: guid(2), concurrencyToken: 'AAAA' }]);
    });

    it('the Post dialog offers "Edit items" to whoever can recall; it recalls and never posts', async () => {
      await open(submitted());
      responses['recall'] = recalledDraft();

      spectator.component.onAction(DOCUMENT_ACTION.Post);
      expect(confirm().$pending()?.request.alternateLabel).toBe('Edit items');

      confirm().alternate();
      expect(calls['recall']).toEqual([{ id: guid(2), concurrencyToken: 'AAAA' }]);
      expect(calls['post']).toBeUndefined();
    });

    it('closing the Post dialog (Esc, outside tap, system Back) changes nothing: still Submitted with Edit next to Post', async () => {
      await open(submitted());

      spectator.component.onAction(DOCUMENT_ACTION.Post);
      confirm().cancel();
      spectator.detectChanges();

      expect(calls['recall']).toBeUndefined();
      expect(calls['post']).toBeUndefined();
      expect(labels()).toEqual(['Edit', 'Post']);
    });

    it('a reader who cannot recall gets "Back" in the Post dialog', async () => {
      await open(submitted({ allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel] }));

      spectator.component.onAction(DOCUMENT_ACTION.Post);

      const request = confirm().$pending()?.request;
      expect(request?.alternateLabel).toBeUndefined();
      expect(request?.cancelLabel).toBe('Back');
    });

    it('a stale answer to Edit is the one E4 banner; a plain 400 reloads the document', async () => {
      await open(submitted());

      commands['recall'].$operation.set(
        failureOperation({ ...STALE, extensions: { code: 'document_changed' } })
      );
      spectator.detectChanges();
      expect(text()).toContain('changed since you opened it');
      expect(detailReload).not.toHaveBeenCalled();

      commands['recall'].$operation.set(
        failureOperation({
          ...STALE,
          kind: API_ERROR_KIND.Validation,
          status: 400,
          detail: 'ISS-WH01-2610-0001 is Approved and can not be taken back to draft.'
        })
      );
      spectator.detectChanges();
      expect(detailReload).toHaveBeenCalledTimes(1);
    });

    it('cancelling an Approved document asks for a reason of 5+ characters and sends it with the version', async () => {
      await open(
        document({
          status: DocumentStatus.Approved,
          allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel]
        })
      );

      spectator.component.onAction(DOCUMENT_ACTION.Cancel);
      const request = confirm().$pending()?.request;
      expect(request?.heading).toBe('Cancel ISS-WH01-2610-0001?');
      expect(request?.body).toContain('approved but not posted yet. Stock does not change');
      expect(request?.reasonRequired).toBe(true);
      expect(request?.minReasonLength).toBe(5);
      expect(request?.reasonLabel).toBe('Reason (at least 5 characters)');

      confirm().confirm('Customer withdrew the order');
      expect(calls['cancel']).toEqual([
        { id: guid(2), concurrencyToken: 'AAAA', reason: 'Customer withdrew the order' }
      ]);
      expect(toasts()).toContain('Document cancelled.');
    });

    it('cancelling a Draft or a Submitted document keeps its optional reason', async () => {
      await open(submitted());

      spectator.component.onAction(DOCUMENT_ACTION.Cancel);

      const request = confirm().$pending()?.request;
      expect(request?.reasonRequired).toBeUndefined();
      expect(request?.reasonLabel).toBe('Reason (optional)');
      expect(request?.body).toBe("This can't be undone.");
    });

    describe('an approved issue that is short on stock (PR-02)', () => {
      const approvedShort = (overrides: Partial<DocumentDto> = {}): DocumentDto =>
        document({
          status: DocumentStatus.Approved,
          approvedByName: 'Lim',
          allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel],
          ...overrides
        });

      it('says it can not be posted now, as an alert with a Cancel document button that opens the dialog', async () => {
        await open(approvedShort());

        const banner = spectator.query('app-banner[role="alert"]');
        expect(banner?.textContent).toContain("Can't be posted now");
        expect(banner?.textContent).toContain(
          'DEMO-001 needs 15, but only 10 are on hand in Main warehouse. Wait for stock or cancel it.'
        );
        const cancel = Array.from(banner?.querySelectorAll('button') ?? []).find(button =>
          button.textContent?.includes('Cancel document')
        );
        spectator.click(cancel as HTMLElement);
        expect(confirm().$pending()?.request.reasonRequired).toBe(true);
      });

      it('adds up the lines of one product', async () => {
        const line = document().lines[0];
        await open(
          approvedShort({
            lines: [
              { ...line, qty: 6, baseQty: 6, available: 10 },
              { ...line, qty: 6, baseQty: 6, available: 10 }
            ]
          })
        );

        expect(text()).toContain("Can't be posted now");
        expect(text()).toContain('needs 12, but only 10');
      });

      it('is not shown with enough stock, with an approved override, or to a reader who can not cancel', async () => {
        const line = document().lines[0];
        await open(approvedShort({ lines: [{ ...line, available: 20 }] }));
        expect(text()).not.toContain("Can't be posted now");

        await open(approvedShort({ negativeStockOverrideReason: 'Customer pre-paid' }));
        expect(text()).not.toContain("Can't be posted now");

        await open(approvedShort({ allowedActions: [DOCUMENT_ACTION.Post] }));
        expect(text()).not.toContain("Can't be posted now");
      });

      it('a worker who made the document is told whom to ask, and has no Cancel', async () => {
        granted.add(PERMISSION.DocumentCreate);
        await open(approvedShort({ allowedActions: [] }));

        expect(text()).toContain('Wrong quantity? Ask Lim to cancel it.');
        expect(spectator.query('.document-editor-bar')).toBeNull();
      });
    });

    describe('NR-11: the stock-changed banner and the page say one thing', () => {
      const conflict = failureOperation({
        ...STALE,
        extensions: { code: 'insufficient_stock', productId: guid(1), available: 2, requested: 15 }
      });

      it('hides "Ready to post" and Post, and tells the submitter to tap Edit', async () => {
        await open(submitted());
        expect(text()).toContain('Ready to post');

        commands['post'].$operation.set(conflict);
        spectator.detectChanges();

        expect(text()).toContain('Stock changed');
        expect(text()).toContain('Tap Edit to lower the quantity');
        expect(text()).not.toContain('Ready to post');
        expect(text()).not.toContain('Wrong quantity? Tap Edit.');
        expect(labels()).toEqual(['Edit']);
      });

      it('an approved document tells the reviewer to wait for stock or cancel, and shows no second banner', async () => {
        await open(
          document({
            status: DocumentStatus.Approved,
            allowedActions: [DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel]
          })
        );

        commands['post'].$operation.set(conflict);
        spectator.detectChanges();

        expect(text()).toContain('Wait for stock or cancel it.');
        expect(text()).not.toContain("Can't be posted now");
        expect(labels().some(label => label === 'Post')).toBe(false);
      });
    });
  });

  describe('the stale-document banner (PS-08)', () => {
    it('goes away after Reload, and the buttons are locked while it shows', async () => {
      await open(document());
      currentToken.mockReturnValue(of('BBBB'));
      spectator.component.onVisibilityChange();
      spectator.detectChanges();

      expect(text()).toContain('changed since you opened it');
      const approve = spectator
        .queryAll('button')
        .find(button => button.textContent?.includes('Approve'));
      expect(approve?.hasAttribute('disabled')).toBe(true);

      opened.set(document({ concurrencyToken: 'BBBB' }));
      spectator.component.onReload();
      spectator.detectChanges();

      expect(text()).not.toContain('changed since you opened it');
    });

    it('is not raised by a tab check that was answered after the user own successful command', async () => {
      await open(
        document({ status: DocumentStatus.Posted, allowedActions: [DOCUMENT_ACTION.Reverse] })
      );
      const answer = new Subject<string | undefined>();
      currentToken.mockReturnValue(answer);
      spectator.component.onVisibilityChange();

      // The reversal finished first: a new copy of the document is open.
      opened.set(
        document({
          status: DocumentStatus.Reversed,
          concurrencyToken: 'CCCC',
          allowedActions: []
        })
      );
      spectator.detectChanges();
      answer.next('CCCC');
      spectator.detectChanges();

      expect(text()).not.toContain('changed since you opened it');
    });
  });
});
