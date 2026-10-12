import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiClampedTextComponent } from './clamped-text.component';

describe('UiClampedTextComponent (L00)', () => {
  let spectator: Spectator<UiClampedTextComponent>;
  const createComponent = createComponentFactory({
    component: UiClampedTextComponent,
    detectChanges: false
  });
  const long = 'W'.repeat(300);

  /** jsdom lays nothing out (scrollHeight is 0), so a test says what the browser would measure. */
  function render(expandable = true): void {
    spectator = createComponent({ props: { text: long, lines: 2, expandable } as never });
    spectator.detectChanges();
  }

  function overflow(): void {
    const body = spectator.query('.ui-clamped-text-body') as HTMLElement;
    Object.defineProperty(body, 'scrollHeight', { value: 80, configurable: true });
    Object.defineProperty(body, 'clientHeight', { value: 40, configurable: true });
    // The component measures after render and when its box is resized: ask it again.
    spectator.setInput('text', `${long}!`);
    spectator.detectChanges();
  }

  it('keeps the whole text in the DOM and clamps it to the given number of lines', () => {
    render();

    const body = spectator.query('.ui-clamped-text-body') as HTMLElement;
    expect(body.textContent?.trim()).toBe(long);
    expect(body.classList).toContain('--clamped');
    expect(body.style.getPropertyValue('--ui-clamp-lines')).toBe('2');
  });

  it('shows no button while the text fits', () => {
    render();

    expect(spectator.query('button')).toBeNull();
  });

  it('offers a real Show more button when the text overflows, and expands and folds it with aria-expanded', () => {
    render();
    overflow();

    const button = spectator.query('button') as HTMLButtonElement;
    expect(button.textContent?.trim()).toBe('Show more');
    expect(button.getAttribute('aria-expanded')).toBe('false');

    spectator.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.textContent?.trim()).toBe('Show less');
    expect(spectator.query('.ui-clamped-text-body')?.classList).not.toContain('--clamped');

    spectator.click(button);
    expect(button.getAttribute('aria-expanded')).toBe('false');
    expect(spectator.query('.ui-clamped-text-body')?.classList).toContain('--clamped');
  });

  it('shows no button when the full value is elsewhere (expandable off)', () => {
    render(false);
    overflow();

    expect(spectator.query('button')).toBeNull();
  });
});
