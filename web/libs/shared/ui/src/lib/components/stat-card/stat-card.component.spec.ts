import { provideRouter } from '@angular/router';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiStatCardComponent } from './stat-card.component';

/** Spectator types `props` by property name ($label) but Angular binds the alias (label): cast at the boundary. */
describe('UiStatCardComponent', () => {
  let spectator: Spectator<UiStatCardComponent>;
  const createComponent = createComponentFactory({
    component: UiStatCardComponent,
    detectChanges: false,
    providers: [provideRouter([])]
  });

  function render(props: Record<string, unknown>): void {
    spectator = createComponent({ props: { label: 'Low stock', value: '4', ...props } as never });
    spectator.detectChanges();
  }

  it('with a link the whole card is ONE link named by its label and its number', () => {
    render({ link: ['/low-stock'] });

    const links = spectator.queryAll('a');
    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('/low-stock');
    expect(links[0].textContent).toMatch(/Low stock\s*4/);
    expect(spectator.query('.ui-stat-card-chevron')).toBeTruthy();
  });

  it('without a link it is plain text: no anchor, no chevron', () => {
    render({});

    expect(spectator.query('a')).toBeNull();
    expect(spectator.query('.ui-stat-card-chevron')).toBeNull();
    expect(spectator.element.textContent).toMatch(/Low stock\s*4/);
  });

  it('attention is drawn with an icon as well as the border colour (never colour alone)', () => {
    render({ attention: true });

    expect(spectator.element.classList).toContain('--attention');
    expect(spectator.query('.ui-stat-card-alert')).toBeTruthy();
  });

  it('no attention, no alert icon', () => {
    render({});

    expect(spectator.query('.ui-stat-card-alert')).toBeNull();
  });
});
