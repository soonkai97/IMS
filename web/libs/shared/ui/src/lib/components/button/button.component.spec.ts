import {
  createComponentFactory,
  createHostFactory,
  Spectator,
  SpectatorHost
} from '@ngneat/spectator/jest';
import { UiIconComponent } from '../icon/icon.component';
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

  it.each(['primary', 'secondary', 'danger', 'danger-outline', 'ghost'])(
    'the %s variant is a modifier on the host',
    variant => {
      render({ variant });

      expect(spectator.element.classList.contains(`--${variant}`)).toBe(true);
    }
  );
});

describe('UiButtonComponent busy and saved states', () => {
  let spectator: Spectator<UiButtonComponent>;
  const createComponent = createComponentFactory({
    component: UiButtonComponent,
    detectChanges: false
  });

  const button = (): HTMLButtonElement => spectator.query('button') as HTMLButtonElement;
  const spinner = (): Element | null => spectator.query('app-spinner');

  beforeEach(() => {
    jest.useFakeTimers();
    spectator = createComponent();
    spectator.detectChanges();
  });

  afterEach(() => jest.useRealTimers());

  function setInput(name: string, value: boolean): void {
    spectator.setInput(name, value);
    spectator.detectChanges();
  }

  it('is busy for assistive technology at once but shows no spinner for a quick answer', () => {
    setInput('loading', true);

    expect(button().getAttribute('aria-busy')).toBe('true');
    expect(button().getAttribute('aria-disabled')).toBe('true');
    jest.advanceTimersByTime(100);
    spectator.detectChanges();
    expect(spinner()).toBeNull();

    setInput('loading', false);
    jest.advanceTimersByTime(1000);
    spectator.detectChanges();
    expect(spinner()).toBeNull();
  });

  it('the busy button keeps its colours: no class changes the fill while it works', () => {
    setInput('loading', true);
    jest.advanceTimersByTime(150);
    spectator.detectChanges();

    expect(spinner()).not.toBeNull();
    expect(spectator.query('.ui-button-face')?.classList.contains('--busy')).toBe(true);
    expect(button().className).toBe('ui-button-control');
  });

  it('shows a tick and "Saved" for 1.5s, then returns to the label', () => {
    setInput('saved', true);

    expect(button().classList.contains('--saved')).toBe(true);
    expect(spectator.query('.ui-button-saved')?.textContent).toContain('Saved');
    expect(spectator.query('.ui-button-saved app-icon')).not.toBeNull();
    // The label keeps its room (the face is hidden, not removed), so the button does not change size.
    expect(spectator.query('.ui-button-face')?.classList.contains('--covered')).toBe(true);

    jest.advanceTimersByTime(1499);
    spectator.detectChanges();
    expect(spectator.query('.ui-button-saved')).not.toBeNull();

    jest.advanceTimersByTime(1);
    spectator.detectChanges();
    expect(spectator.query('.ui-button-saved')).toBeNull();
    expect(button().classList.contains('--saved')).toBe(false);
    expect(spectator.query('.ui-button-face')?.classList.contains('--covered')).toBe(false);
  });
});

describe('UiButtonComponent label and icon slots', () => {
  let spectator: SpectatorHost<UiButtonComponent>;
  const createHost = createHostFactory({
    component: UiButtonComponent,
    imports: [UiIconComponent]
  });

  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  it('keeps the label while busy, and the spinner takes the place of the leading icon', () => {
    spectator = createHost(
      `<app-button [loading]="true"><app-icon name="plus" />Save draft</app-button>`
    );
    jest.advanceTimersByTime(150);
    spectator.detectChanges();

    expect(spectator.query('button')?.textContent).toContain('Save draft');
    expect(spectator.query('.ui-button-face app-spinner')).not.toBeNull();
    // The projected icon sits in its own slot, which the busy modifier removes from the layout.
    expect(spectator.query('.ui-button-icon app-icon')).not.toBeNull();
    expect(spectator.query('.ui-button-face')?.classList.contains('--busy')).toBe(true);
  });
});
