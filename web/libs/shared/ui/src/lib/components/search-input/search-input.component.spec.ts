import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiSearchInputComponent } from './search-input.component';

describe('UiSearchInputComponent', () => {
  let spectator: Spectator<UiSearchInputComponent>;
  const createComponent = createComponentFactory({
    component: UiSearchInputComponent,
    detectChanges: false
  });

  function render(props: Record<string, unknown> = {}): void {
    spectator = createComponent({ props: { label: 'Search', ...props } as never });
    spectator.detectChanges();
  }

  it('marks the native input for focus-on-open on request (a dialog then focuses it)', () => {
    render({ focusOnOpen: true });

    expect(spectator.query('input')?.hasAttribute('data-autofocus')).toBe(true);
  });

  it('is not autofocused by default', () => {
    render();

    expect(spectator.query('input')?.hasAttribute('data-autofocus')).toBe(false);
  });

  it('limits the length of what can be typed or pasted (EA-16)', () => {
    render();

    expect(spectator.query('input')?.getAttribute('maxlength')).toBe('100');
  });

  it('Enter emits the exact text, which is how a keyboard-wedge scanner delivers a barcode', () => {
    render();
    const codes: string[] = [];
    spectator.output('$submitted').subscribe(code => codes.push(code as string));

    spectator.typeInElement('9556000000011', 'input');
    spectator.dispatchKeyboardEvent('input', 'keydown', 'Enter');

    expect(codes).toEqual(['9556000000011']);
  });

  it('draws one clear button of its own', () => {
    render();
    spectator.typeInElement('abc', 'input');
    spectator.detectChanges();

    expect(spectator.queryAll('button.ui-search-input-clear')).toHaveLength(1);
  });
});
