import { createHostFactory, SpectatorHost } from '@ngneat/spectator/jest';
import { UiCardComponent } from './card.component';

describe('UiCardComponent', () => {
  let spectator: SpectatorHost<UiCardComponent>;
  const createHost = createHostFactory(UiCardComponent);

  it('shows the heading and the projected content', () => {
    spectator = createHost(`<app-card heading="Details"><p class="inside">Hello</p></app-card>`);

    expect(spectator.query('.ui-card-heading')?.textContent).toBe('Details');
    expect(spectator.query('.inside')?.textContent).toBe('Hello');
  });

  it('renders no heading element without a heading', () => {
    spectator = createHost(`<app-card><p>Hello</p></app-card>`);

    expect(spectator.query('.ui-card-heading')).toBeNull();
  });

  it('shows a 500-character heading whole, uncut', () => {
    const heading = 'W'.repeat(500);
    spectator = createHost(`<app-card [heading]="heading"><p>Hello</p></app-card>`, {
      hostProps: { heading }
    });

    const node = spectator.query('.ui-card-heading');
    expect(node?.textContent).toBe(heading);
    expect(node?.hasAttribute('title')).toBe(false);
  });
});
