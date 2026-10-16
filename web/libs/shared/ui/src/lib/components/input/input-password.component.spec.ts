import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiInputComponent } from './input.component';

describe('UiInputComponent password field (R25)', () => {
  let spectator: Spectator<UiInputComponent>;
  const createComponent = createComponentFactory({
    component: UiInputComponent,
    detectChanges: false
  });

  function render(props: Record<string, unknown> = {}): void {
    spectator = createComponent({ props: { type: 'password', ...props } as never });
    spectator.detectChanges();
  }

  const input = (): HTMLInputElement => spectator.query('input') as HTMLInputElement;
  const toggle = (): HTMLButtonElement => spectator.query('button') as HTMLButtonElement;

  it('the show-password button has one constant name and carries the state in aria-pressed', () => {
    render();
    expect(toggle().getAttribute('aria-label')).toBe('Show password');
    expect([toggle().getAttribute('aria-pressed'), input().type]).toEqual(['false', 'password']);

    spectator.click(toggle());

    expect(toggle().getAttribute('aria-label')).toBe('Show password');
    expect([toggle().getAttribute('aria-pressed'), input().type]).toEqual(['true', 'text']);
  });

  it('a pointer press does not take the focus (the phone keyboard stays open), and the text clears the button', () => {
    render();
    const down = new MouseEvent('mousedown', { bubbles: true, cancelable: true });

    toggle().dispatchEvent(down);

    expect(down.defaultPrevented).toBe(true);
    expect(input().classList.contains('--with-toggle')).toBe(true);
  });

  it('noSuggestions turns off capitals, correction and spell check; name is passed on', () => {
    render({ noSuggestions: true, name: 'password' });
    expect(
      ['autocapitalize', 'autocorrect', 'spellcheck', 'name'].map(a => input().getAttribute(a))
    ).toEqual(['none', 'off', 'false', 'password']);

    render();
    expect(input().hasAttribute('autocapitalize') || input().hasAttribute('name')).toBe(false);
  });
});
