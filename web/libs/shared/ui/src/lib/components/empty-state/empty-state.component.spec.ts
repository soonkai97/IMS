import { createHostFactory, SpectatorHost } from '@ngneat/spectator/jest';
import { UiEmptyStateComponent } from './empty-state.component';

describe('UiEmptyStateComponent', () => {
  let spectator: SpectatorHost<UiEmptyStateComponent, { icon?: string; message?: string }>;
  const createHost = createHostFactory(UiEmptyStateComponent);

  it('puts the 40px icon in a disc, then the message, then the projected action', () => {
    spectator = createHost(
      `<app-empty-state icon="lock" message="Your role can view receipts."><button class="action">New</button></app-empty-state>`
    );

    const children = Array.from(spectator.element.children).map(child => child.className);
    expect(children).toEqual(['ui-empty-state-disc', 'ui-empty-state-message', 'action']);
    expect(spectator.query('.ui-empty-state-disc app-icon')?.getAttribute('data-size')).toBe('40');
  });

  it.each(['info', 'search', 'lock', 'alert'])('draws the %s icon', icon => {
    spectator = createHost(`<app-empty-state [icon]="icon" message="Nothing here." />`, {
      hostProps: { icon }
    });

    expect(spectator.query('.ui-empty-state-disc svg')).toBeTruthy();
  });

  it('defaults to the info icon', () => {
    spectator = createHost(`<app-empty-state message="Nothing here." />`);

    expect(spectator.query('.ui-empty-state-disc svg')).toBeTruthy();
  });

  it('shows a 500-character message whole, uncut', () => {
    const message = `No results for "${'W'.repeat(500)}".`;
    spectator = createHost(`<app-empty-state icon="search" [message]="message" />`, {
      hostProps: { message }
    });

    const node = spectator.query('.ui-empty-state-message');
    expect(node?.textContent).toBe(message);
    expect(node?.hasAttribute('title')).toBe(false);
  });
});
