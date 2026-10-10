import { signal } from '@angular/core';
import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentCounts, StockSummary } from '@ims/api';
import { PermissionService } from '@ims/core/identity';
import { DocumentsStore } from '@ims/features/documents';
import { StockStore } from '@ims/features/stock';
import { PERMISSION } from '@ims/models';
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
  const createComponent = createComponentFactory({
    component: HomePageComponent,
    detectChanges: false,
    providers: [
      provideRouter([]),
      {
        provide: DocumentsStore,
        useValue: { counts: { $data: counts, load: countsLoad, reload: jest.fn() } }
      },
      {
        provide: StockStore,
        useValue: {
          summary: {
            $data: signal(SUMMARY),
            $isLoading: signal(false),
            $isFailed: signal(false),
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

  beforeEach(() => countsLoad.mockClear());

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

  it('asks for no numbers when the user cannot read stock', () => {
    render();

    expect(countsLoad).not.toHaveBeenCalled();
  });
});
