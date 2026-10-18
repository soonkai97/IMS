import { computed, signal } from '@angular/core';
import { NEVER, of } from 'rxjs';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import {
  DocumentCounts,
  DocumentsClient,
  DocumentListItem,
  DocumentType,
  PagedResultOfDocumentListItem,
  StockSummary
} from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { PermissionService } from '@ims/core/identity';
import { DocumentsStore } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { ApiError, ApiOperation, IDLE_OPERATION, PERMISSION, failureOperation } from '@ims/models';
import { HomeFeedStore } from './home-feed.store';
import { HomePageComponent } from './home.component';

const COUNTS: DocumentCounts = {
  returnedToMe: 2,
  myDrafts: 5,
  postedToday: 9,
  approvalsWaiting: 3
};
/** One returned receipt, one plain draft, one returned issue. */
const DRAFTS: Partial<DocumentListItem>[] = [
  {
    id: 'r1',
    type: DocumentType.Receipt,
    no: 'RCV-WH01-2610-0001',
    isReturned: true,
    partnerName: 'Acme Sdn Bhd',
    warehouseName: 'Main WH',
    createdAt: '2026-10-09T08:05:00Z'
  },
  { id: 'd1', type: DocumentType.Receipt, no: 'RCV-WH01-2610-0002', isReturned: false },
  {
    id: 'i1',
    type: DocumentType.Issue,
    no: 'ISS-WH01-2610-0003',
    isReturned: true,
    partnerName: undefined,
    warehouseName: 'Main WH',
    createdAt: '2026-10-10T08:05:00Z'
  }
];
const SUMMARY = { totalValue: 40, skuCount: 1, lowCount: 4 } as StockSummary;

describe('HomePageComponent', () => {
  let spectator: Spectator<HomePageComponent>;
  const counts = signal<DocumentCounts | undefined>(COUNTS);
  const granted = new Set<string>();
  const countsLoad = jest.fn();
  const countsOperation = signal<ApiOperation>(IDLE_OPERATION);
  const drafts = signal<PagedResultOfDocumentListItem | undefined>(undefined);
  const draftsOperation = signal<ApiOperation>(IDLE_OPERATION);
  const draftsLoad = jest.fn();
  const feedMock = {
    drafts: {
      $data: drafts,
      $operation: draftsOperation,
      $isLoading: signal(false),
      $isFailed: computed(() => draftsOperation().status === 'failure'),
      load: draftsLoad,
      reload: jest.fn()
    }
  };
  const baseProviders = [
    provideRouter([]),
    {
      provide: DocumentsStore,
      useValue: {
        counts: {
          $data: counts,
          $operation: countsOperation,
          $isFailed: computed(() => countsOperation().status === 'failure'),
          load: countsLoad,
          reload: jest.fn()
        }
      }
    },
    {
      provide: StockStore,
      useValue: {
        summary: {
          $data: signal(SUMMARY),
          $isLoading: signal(false),
          $isFailed: signal(false),
          $operation: signal<ApiOperation>(IDLE_OPERATION),
          load: jest.fn(),
          reload: jest.fn()
        }
      }
    },
    {
      provide: PermissionService,
      useValue: {
        has: (permission: string) => granted.has(permission),
        hasAny: (permissions: readonly string[]) =>
          permissions.some(permission => granted.has(permission)),
        $displayName: signal('Pat'),
        $seesAllWarehouses: signal(true),
        $warehouseIds: signal<number[]>([])
      }
    }
  ];
  /** The feed store belongs to the component (providers: [HomeFeedStore]), so the mock goes in as a component provider. */
  const createComponent = createComponentFactory({
    component: HomePageComponent,
    detectChanges: false,
    providers: baseProviders,
    componentProviders: [{ provide: HomeFeedStore, useValue: feedMock }]
  });

  function render(...permissions: string[]): string {
    granted.clear();
    permissions.forEach(permission => granted.add(permission));
    spectator = createComponent();
    spectator.detectChanges();
    return tiles().join(' ; ');
  }

  /** "Label value" of every stat tile. */
  function tiles(): string[] {
    return spectator
      .queryAll('app-stat-card')
      .map(card =>
        [card.querySelector('.ui-stat-card-label'), card.querySelector('.ui-stat-card-value')]
          .map(part => part?.textContent?.trim() ?? '')
          .join(' ')
      );
  }

  beforeEach(() => {
    countsLoad.mockClear();
    draftsLoad.mockClear();
    countsOperation.set(IDLE_OPERATION);
    draftsOperation.set(IDLE_OPERATION);
    counts.set(COUNTS);
    drafts.set({ items: DRAFTS as DocumentListItem[] } as PagedResultOfDocumentListItem);
  });

  it('warehouse staff and clerks see returned documents and their drafts, nothing about approvals', () => {
    const text = render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

    expect(text).toContain('Returned to me 2');
    expect(text).toContain('My drafts 5');
    expect(text).not.toContain('Waiting for approval');
    expect(text).not.toContain('Posted today');
  });

  it('a supervisor sees what waits for approval and what was posted today, from ONE counts call', () => {
    const text = render(
      PERMISSION.StockRead,
      PERMISSION.DocumentCreate,
      PERMISSION.DocumentApprove,
      PERMISSION.DocumentPost
    );

    expect(text).toContain('Waiting for approval 3');
    expect(text).toContain('Posted today 9');
    expect(text).toContain('Low stock 4');
    expect(countsLoad).toHaveBeenCalledTimes(1);
  });

  it('the Owner (who holds only the large approval tier) reviews but does not post: approvals yes, posted today no, no drafts', () => {
    const text = render(PERMISSION.StockRead, PERMISSION.DocumentApproveLarge);

    expect(text).toContain('Waiting for approval 3');
    expect(text).not.toContain('Posted today');
    expect(text).not.toContain('My drafts');
  });

  describe('the stock value tile (valuation.view, docs/design/mvp-ux.md 1.4)', () => {
    it('is shown to a supervisor, who holds valuation.view', () => {
      const text = render(
        PERMISSION.StockRead,
        PERMISSION.DocumentApprove,
        PERMISSION.DocumentPost,
        PERMISSION.CostView,
        PERMISSION.ValuationView
      );

      expect(text).toContain('Stock value (RM) 40.00');
      expect(text).toContain('Low stock 4');
    });

    it('is not rendered for warehouse staff: no tile, no empty value, the other tiles stay', () => {
      const text = render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(text).not.toContain('Stock value');
      expect(text).not.toContain('0.00');
      expect(text).toContain('Low stock 4');
      expect(text).toContain('My drafts 5');
    });

    it('is not rendered for a clerk: cost.view alone does not unlock a value', () => {
      const text = render(PERMISSION.StockRead, PERMISSION.DocumentCreate, PERMISSION.CostView);

      expect(text).not.toContain('Stock value');
    });
  });

  it('asks for no numbers when the user cannot read stock', () => {
    render();

    expect(countsLoad).not.toHaveBeenCalled();
  });

  it('flags both loads handledInline, and a failed counts call alone is one alert with the mapped sentence (R11)', () => {
    const error = { status: 503, message: 'The database is not available.' } as ApiError;
    countsOperation.set(failureOperation(error));
    render(PERMISSION.StockRead);

    expect(isHandledInline(countsLoad.mock.calls[0][1], 503)).toBe(true);
    const alerts = spectator.queryAll('[role="alert"]');
    expect(alerts).toHaveLength(1);
    expect(alerts[0].textContent).toContain('The database is not available.');
  });

  describe('every tile opens its list', () => {
    /** href of the tile link whose text contains the label. */
    function hrefOf(label: string): string | null | undefined {
      return spectator
        .queryAll('app-stat-card a')
        .find(link => link.textContent?.includes(label))
        ?.getAttribute('href');
    }

    it('a supervisor: approvals, reports (posted today), low stock, stock', () => {
      render(
        PERMISSION.StockRead,
        PERMISSION.DocumentApprove,
        PERMISSION.DocumentPost,
        PERMISSION.ReportsRead,
        PERMISSION.ValuationView
      );

      expect(hrefOf('Waiting for approval')).toBe('/approvals');
      expect(hrefOf('Posted today')).toBe('/reports');
      expect(hrefOf('Low stock')).toBe('/low-stock');
      expect(hrefOf('Stock value')).toBe('/stock');
      expect(hrefOf('Products with stock')).toBe('/stock');
    });

    it('posted today falls back to the receipts list without reports.read', () => {
      render(PERMISSION.StockRead, PERMISSION.DocumentApprove, PERMISSION.DocumentPost);

      expect(hrefOf('Posted today')).toBe('/receipts');
    });

    it('the Owner (large tier only) reaches the approvals through hasAny, not through a role name', () => {
      render(PERMISSION.StockRead, PERMISSION.DocumentApproveLarge);

      expect(hrefOf('Waiting for approval')).toBe('/approvals');
    });

    it('drafts open the list of the first row type; returned drafts the list of the first returned row', () => {
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);
      expect(hrefOf('My drafts')).toBe('/receipts');
      expect(hrefOf('Returned to me')).toBe('/receipts');

      drafts.set({ items: [DRAFTS[2]] } as PagedResultOfDocumentListItem);
      spectator = createComponent();
      spectator.detectChanges();
      expect(hrefOf('Returned to me')).toBe('/issues');
      expect(hrefOf('My drafts')).toBe('/issues');
    });

    it('the accessible name of a tile is its label and its number', () => {
      render(PERMISSION.StockRead, PERMISSION.DocumentApprove, PERMISSION.DocumentPost);

      const link = spectator.query('app-stat-card a') as HTMLAnchorElement;
      expect(link.textContent).toMatch(/Waiting for approval\s*3/);
    });
  });

  describe('Returned to me list', () => {
    function rows(): HTMLElement[] {
      return spectator.queryAll('.home-page-row');
    }

    it('lists the returned drafts only, each a link to its document, dates as dd/MM/yyyy', () => {
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(rows()).toHaveLength(2);
      expect(rows()[0].getAttribute('href')).toBe('/receipts/r1');
      expect(rows()[1].getAttribute('href')).toBe('/issues/i1');
      expect(rows()[0].textContent).toContain('Acme Sdn Bhd');
      expect(rows()[0].textContent).toContain('09/10/2026');
      expect(rows()[0].textContent).toContain('Returned');
      expect(rows()[1].textContent).toContain('Main WH');
      expect(draftsLoad).toHaveBeenCalledTimes(1);
    });

    it('is hidden when nothing is returned, and a user who cannot create asks for no drafts', () => {
      counts.set({ ...COUNTS, returnedToMe: 0 });
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);
      expect(spectator.query('.home-page-returned')).toBeNull();

      spectator.fixture.destroy();
      counts.set(COUNTS);
      draftsLoad.mockClear();
      render(PERMISSION.StockRead, PERMISSION.DocumentApprove);
      expect(spectator.query('.home-page-returned')).toBeNull();
      expect(draftsLoad).not.toHaveBeenCalled();
    });

    it('asks for the drafts only when the counts say something is returned (the common case is none)', () => {
      counts.set({ ...COUNTS, returnedToMe: 0 });
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(draftsLoad).not.toHaveBeenCalled();
      expect(spectator.query('.home-page-returned')).toBeNull();
    });

    it('a long partner name is clamped to two lines and the number stays whole', () => {
      drafts.set({
        items: [{ ...DRAFTS[0], partnerName: 'P'.repeat(300) }]
      } as PagedResultOfDocumentListItem);
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(spectator.query('.home-page-row-meta')?.textContent).toContain('P'.repeat(300));
      expect(spectator.query('.home-page-row-no')?.textContent).toContain('RCV-WH01-2610-0001');
    });

    it('a failed list is its own alert with Retry and the tiles stay (failure isolation)', () => {
      draftsOperation.set(failureOperation({ status: 503, message: 'Try later.' } as ApiError));
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(spectator.queryAll('[role="alert"]')).toHaveLength(1);
      expect(tiles().join(' ; ')).toContain('Returned to me 2');
    });

    it('says where to look when the returned drafts are older than the newest 25', () => {
      drafts.set({ items: [DRAFTS[1]] } as PagedResultOfDocumentListItem);
      render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

      expect(rows()).toHaveLength(0);
      expect(spectator.query('.home-page-returned')?.textContent).toContain('Open the list');
    });
  });

  it('warehouse staff see no money on the page: no 2-decimal amount, no value, none in a title or aria-label', () => {
    render(PERMISSION.StockRead, PERMISSION.DocumentCreate);

    const money = /\d[\d,]*\.\d{2}\b/;
    const text = spectator.element.textContent ?? '';
    expect(text).not.toMatch(money);
    expect(text.toLowerCase()).not.toContain('value');
    spectator
      .queryAll('[aria-label], [title]')
      .map(element => `${element.getAttribute('aria-label')} ${element.getAttribute('title')}`)
      .forEach(attribute => expect(attribute).not.toMatch(money));
  });
});

/** The real feed store over a fake API client (its own factory: Spectator factories apply to every test of their describe). */
describe('HomePageComponent: the drafts of one user never reach the next one on the same tab', () => {
  const listCall = jest.fn();
  const createComponent = createComponentFactory({
    component: HomePageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      { provide: DocumentsClient, useValue: { list: listCall } },
      {
        provide: DocumentsStore,
        useValue: {
          counts: {
            $data: signal<DocumentCounts>(COUNTS),
            $operation: signal<ApiOperation>(IDLE_OPERATION),
            $isFailed: signal(false),
            load: jest.fn(),
            reload: jest.fn()
          }
        }
      },
      {
        provide: StockStore,
        useValue: {
          summary: {
            $data: signal(SUMMARY),
            $isLoading: signal(false),
            $isFailed: signal(false),
            $operation: signal<ApiOperation>(IDLE_OPERATION),
            load: jest.fn(),
            reload: jest.fn()
          }
        }
      },
      {
        provide: PermissionService,
        useValue: {
          has: (permission: string) =>
            [PERMISSION.StockRead, PERMISSION.DocumentCreate].includes(permission as never),
          hasAny: () => false,
          $displayName: signal('Pat'),
          $seesAllWarehouses: signal(true),
          $warehouseIds: signal<number[]>([])
        }
      }
    ]
  });

  it('after sign-out the next Home shows no row and no name while its own call is pending', () => {
    listCall.mockReturnValueOnce(of({ items: DRAFTS }));

    // The clerk's Home loads two returned drafts.
    const clerk = createComponent();
    clerk.detectChanges();
    expect(clerk.queryAll('.home-page-row')).toHaveLength(2);
    expect(clerk.element.textContent).toContain('Acme Sdn Bhd');
    clerk.fixture.destroy();

    // Sign-out is a navigation: the next user's Home is a new component whose own call has not answered yet.
    listCall.mockReturnValueOnce(NEVER);
    const staff = createComponent();
    staff.detectChanges();

    expect(listCall).toHaveBeenCalledTimes(2);
    expect(staff.queryAll('.home-page-row')).toHaveLength(0);
    expect(staff.element.textContent).not.toContain('Acme Sdn Bhd');
    expect(staff.element.textContent).not.toContain('RCV-WH01-2610-0001');
  });
});
