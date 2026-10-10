import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiButtonComponent } from './button.component';

describe('UiButtonComponent states', () => {
  let spectator: Spectator<UiButtonComponent>;
  const createComponent = createComponentFactory({
    component: UiButtonComponent,
    detectChanges: false
  });
  const clicks = (): number[] => {
    const seen: number[] = [];
    spectator.output('$clicked').subscribe(() => seen.push(1));
    return seen;
  };

  function render(props: Record<string, unknown>): HTMLButtonElement {
    spectator = createComponent({ props: props as never });
    spectator.detectChanges();
    return spectator.query('button') as HTMLButtonElement;
  }

  it('a busy button stays focusable (a disabled one would throw the focus to the page top) but does nothing', () => {
    const button = render({ loading: true });
    const seen = clicks();

    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-busy')).toBe('true');
    expect(button.getAttribute('aria-disabled')).toBe('true');
    button.focus();
    expect(document.activeElement).toBe(button);
    spectator.click(button);
    expect(seen).toEqual([]);
  });

  it('a disabled button is natively disabled', () => {
    const button = render({ disabled: true });

    expect(button.disabled).toBe(true);
  });

  it('aria-disabled keeps the button reachable and tied to its reason, and a press does nothing (EA-22)', () => {
    const button = render({ ariaDisabled: true, describedBy: 'why' });
    const seen = clicks();

    expect(button.disabled).toBe(false);
    expect(button.getAttribute('aria-disabled')).toBe('true');
    expect(button.getAttribute('aria-describedby')).toBe('why');
    spectator.click(button);
    expect(seen).toEqual([]);
  });

  it('an ordinary button emits its click', () => {
    const button = render({});
    const seen = clicks();

    spectator.click(button);

    expect(seen).toEqual([1]);
  });

  it('a button that toggles something reports expanded and what it controls', () => {
    const button = render({ expanded: true, controls: 'panel', popup: 'dialog' });

    expect(button.getAttribute('aria-expanded')).toBe('true');
    expect(button.getAttribute('aria-controls')).toBe('panel');
    expect(button.getAttribute('aria-haspopup')).toBe('dialog');
  });
});
