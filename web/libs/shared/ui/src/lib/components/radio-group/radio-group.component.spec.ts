import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiRadioGroupComponent } from './radio-group.component';

describe('UiRadioGroupComponent accessibility (NV-09, EA-06)', () => {
  let spectator: Spectator<UiRadioGroupComponent>;
  const createComponent = createComponentFactory({
    component: UiRadioGroupComponent,
    detectChanges: false
  });

  function render(props: Record<string, unknown> = {}): void {
    spectator = createComponent({
      props: {
        legend: 'Purpose',
        options: [
          { value: 'Sale', label: 'Sale' },
          { value: 'Use', label: 'Use' }
        ],
        ...props
      } as never
    });
    spectator.detectChanges();
  }

  it('says (required) in the legend, like a form field', () => {
    render({ required: true });

    expect(spectator.query('legend')?.textContent).toContain('(required)');
  });

  it('has no required mark unless asked', () => {
    render();

    expect(spectator.query('legend')?.textContent).not.toContain('required');
  });

  it('an invalid group is marked and tied to its error text', () => {
    render({ invalid: true, errorId: 'purpose-error' });

    const set = spectator.query('fieldset');
    expect(set?.getAttribute('aria-invalid')).toBe('true');
    expect(set?.getAttribute('aria-describedby')).toBe('purpose-error');
  });

  it('a valid group carries neither attribute', () => {
    render({ errorId: 'purpose-error' });

    const set = spectator.query('fieldset');
    expect(set?.hasAttribute('aria-invalid')).toBe(false);
    expect(set?.hasAttribute('aria-describedby')).toBe(false);
  });
});
