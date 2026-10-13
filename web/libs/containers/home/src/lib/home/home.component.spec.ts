import { computed, signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentCounts, StockSummary } from '@ims/api';
import { isHandledInline } from '@ims/core/http';
import { PermissionService } from '@ims/core/identity';
import { DocumentsStore } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { ApiError, ApiOperation, IDLE_OPERATION, PERMISSION, failureOperation } from '@ims/models';
import { HomePageComponent } from './home.component';

const COUNTS: DocumentCounts = {
  returnedToMe: 2,
  myDrafts: 5,
  postedToday: 9,
  approvalsWaiting: 3
};
const SUMMARY = { totalValue: 40, skuCount: 1, lowCount: 4 } as StockSummary;

describe('HomePageComponent', () => {
  let spectator: Spectator<HomePageComponent>;
  const counts = signal<DocumentCounts | undefined>(COUNTS);
  const granted = new Set<string>();
  const countsLoad = jest.fn();
  const countsOperation = signal<ApiOperation>(IDLE_OPERATION);
  const createComponent = createComponentFactory({
    component: HomePageComponent,
    detectChanges: false,
    providers: [
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
          $displayName: signal('Pat'),
          $seesAllWarehouses: signal(true),
          $warehouseIds: signal<number[]>([])
        }
      }
    ]
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
    countsOperation.set(IDLE_OPERATION);
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

  it('the Owner reviews but does not post: approvals yes, posted today no, no drafts', () => {
    const text = render(PERMISSION.StockRead, PERMISSION.DocumentApprove);

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
});
