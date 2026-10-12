import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiSkeletonComponent } from './skeleton.component';

describe('UiSkeletonComponent', () => {
  let spectator: Spectator<UiSkeletonComponent>;
  const createComponent = createComponentFactory({ component: UiSkeletonComponent });

  it('is a busy status region with five hidden placeholder rows and one spoken label', () => {
    spectator = createComponent();

    expect(spectator.element.getAttribute('role')).toBe('status');
    expect(spectator.element.getAttribute('aria-busy')).toBe('true');
    expect(spectator.queryAll('.ui-skeleton-row')).toHaveLength(5);
    spectator.queryAll('.ui-skeleton-row').forEach(row => {
      expect(row.getAttribute('aria-hidden')).toBe('true');
    });
    expect(spectator.query('.sr-only')?.textContent).toBe('Loading');
  });

  it('renders the requested number of rows', () => {
    spectator = createComponent({ props: { count: 3 } as never });

    expect(spectator.queryAll('.ui-skeleton-row')).toHaveLength(3);
  });
});
