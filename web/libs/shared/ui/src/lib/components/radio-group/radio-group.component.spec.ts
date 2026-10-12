import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiRadioGroupComponent } from './radio-group.component';

// A non-breaking space keeps the star on the line of the last word (RQ-06).
const NBSP = String.fromCharCode(0xa0);

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

  it('a required group puts a red asterisk and the word "required" in the legend (D4)', () => {
    render({ required: true });

    const legend = spectator.query('legend');
    const star = legend?.querySelector('.ui-radio-group-asterisk');
    expect(star?.getAttribute('aria-hidden')).toBe('true');
    expect(legend?.querySelector('.ui-radio-group-sr')?.textContent).toBe('required');
    expect(legend?.textContent).not.toContain('(required)');
    // The non-breaking space keeps the star on the line of the last word (RQ-06).
    expect(legend?.textContent).toContain(`Purpose${NBSP}*`);
  });

  it('the radios do not carry required themselves: the legend says it once, not once per option', () => {
    render({ required: true });

    const radios = spectator.queryAll('input[type=radio]');
    expect(radios).toHaveLength(2);
    expect(radios.some(radio => radio.hasAttribute('required'))).toBe(false);
  });

  it('has no required mark unless asked', () => {
    render();

    expect(spectator.query('legend')?.textContent?.trim()).toBe('Purpose');
    expect(spectator.query('.ui-radio-group-asterisk')).toBeNull();
    expect(spectator.query('.ui-radio-group-sr')).toBeNull();
  });

  it('an invalid group is marked and tied to its error text', () => {
    render({ invalid: true, errorId: 'purpose-error' });

    const set = spectator.query('fieldset');
    expect(set?.getAttribute('aria-invalid')).toBe('true');
    expect(set?.getAttribute('aria-describedby')).toBe('purpose-error');
  });

  it('the default group is not segmented', () => {
    render();

    expect(spectator.query('.ui-radio-group-options')?.classList.contains('--segmented')).toBe(
      false
    );
  });

  it('a segmented group is still radios in one fieldset, so the arrow keys and Space work natively', () => {
    render({ variant: 'segmented' });

    expect(spectator.query('.ui-radio-group-options')?.classList.contains('--segmented')).toBe(
      true
    );
    const radios = spectator.queryAll('input[type=radio]') as HTMLInputElement[];
    expect(radios).toHaveLength(2);
    expect(new Set(radios.map(radio => radio.name)).size).toBe(1);
    expect(radios.every(radio => radio.classList.contains('--segmented'))).toBe(true);
  });

  it('choosing an option reports its value and marks it checked', () => {
    render({ variant: 'segmented' });
    const seen: (string | number)[] = [];
    spectator.component.registerOnChange(value => seen.push(value));

    spectator.click(spectator.queryAll('input[type=radio]')[1]);

    expect(seen).toEqual(['Use']);
    expect(spectator.queryAll('.ui-radio-group-option')[1].classList.contains('--checked')).toBe(
      true
    );
  });

  it('a valid group carries neither attribute', () => {
    render({ errorId: 'purpose-error' });

    const set = spectator.query('fieldset');
    expect(set?.hasAttribute('aria-invalid')).toBe(false);
    expect(set?.hasAttribute('aria-describedby')).toBe(false);
  });
});
