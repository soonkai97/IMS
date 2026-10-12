import { FormControl, Validators } from '@angular/forms';
import {
  createComponentFactory,
  createHostFactory,
  Spectator,
  SpectatorHost
} from '@ngneat/spectator/jest';
import { UiInputComponent } from '../input/input.component';
import { UiSelectComponent } from '../select/select.component';
import { UiTextareaComponent } from '../textarea/textarea.component';
import { UiFormFieldComponent } from './form-field.component';

describe('UiFormFieldComponent error wording', () => {
  let spectator: Spectator<UiFormFieldComponent>;
  const createComponent = createComponentFactory({
    component: UiFormFieldComponent,
    detectChanges: false
  });

  function render(control: FormControl, messages: Record<string, string> = {}): void {
    spectator = createComponent({ props: { label: 'Quantity', control, messages } as never });
    control.markAsTouched();
    spectator.detectChanges();
  }

  it('uses the field-specific sentence for the error that occurred (NV-09)', () => {
    render(new FormControl(-5, [Validators.min(0.001)]), { min: 'Enter a quantity above 0.' });

    expect(spectator.query('.ui-form-field-error')?.textContent).toContain(
      'Enter a quantity above 0.'
    );
  });

  it('the error has an icon as well as text, so colour is never the only cue (FR-08)', () => {
    render(new FormControl(null, [Validators.required]));

    const error = spectator.query('.ui-form-field-error');
    expect(error?.querySelector('app-icon[name="alert"]')).not.toBeNull();
    expect(error?.textContent).toContain('Invalid value (required)');
  });

  it('falls back to the kit sentence for errors it has no field wording for', () => {
    render(new FormControl(null, [Validators.required]), { min: 'Enter a quantity above 0.' });

    expect(spectator.query('.ui-form-field-error')?.textContent).toContain(
      'Invalid value (required)'
    );
  });
});

// A non-breaking space keeps the star on the line of the last word (RQ-06).
const NBSP = String.fromCharCode(0xa0);

/** What a screen reader says for the label: everything except what is `aria-hidden`, spaces collapsed. */
function accessibleLabel(label: Element | null): string {
  if (!label) {
    return '';
  }
  const copy = label.cloneNode(true) as Element;
  copy.querySelectorAll('[aria-hidden="true"]').forEach(hidden => hidden.remove());
  return (copy.textContent ?? '').replace(/\s+/g, ' ').trim();
}

describe('UiFormFieldComponent required marker (D4, RQ-01 to RQ-06)', () => {
  let spectator: SpectatorHost<UiFormFieldComponent>;
  const createHost = createHostFactory({
    component: UiFormFieldComponent,
    imports: [UiInputComponent, UiSelectComponent, UiTextareaComponent]
  });

  const label = (): Element | null => spectator.query('label');
  const asterisk = (): Element | null => spectator.query('.ui-form-field-asterisk');

  it('a required field shows a red asterisk that screen readers skip', () => {
    spectator = createHost(
      `<app-form-field label="Quantity" [required]="true"><app-input /></app-form-field>`
    );

    expect(asterisk()?.textContent?.trim()).toBe('*');
    expect(asterisk()?.getAttribute('aria-hidden')).toBe('true');
  });

  it('the name is the label alone and the control exposes the state, so "required" is read once, never "star"', () => {
    spectator = createHost(
      `<app-form-field label="Email or username" [required]="true"><app-input /></app-form-field>`
    );

    // A visually hidden "required" in the label would be announced twice next to the state (review of R02).
    expect(accessibleLabel(label())).toBe('Email or username');
    expect(label()?.textContent).not.toContain('required');
    expect(spectator.query<HTMLInputElement>('input')?.required).toBe(true);
  });

  it('the asterisk is glued to the last word of the label so it never wraps alone (RQ-06)', () => {
    spectator = createHost(
      `<app-form-field label="Quantity" [required]="true"><app-input /></app-form-field>`
    );

    expect(label()?.textContent).toContain(`Quantity${NBSP}*`);
  });

  it('an optional field has no asterisk', () => {
    spectator = createHost(`<app-form-field label="Remark"><app-input /></app-form-field>`);

    expect(asterisk()).toBeNull();
    expect(accessibleLabel(label())).toBe('Remark');
  });

  it('puts required on a text input, a select and a textarea', () => {
    spectator = createHost(
      `<div>
        <app-form-field label="A" [required]="true"><app-input /></app-form-field>
        <app-form-field label="B" [required]="true"><app-select [options]="[]" /></app-form-field>
        <app-form-field label="C" [required]="true"><app-textarea /></app-form-field>
      </div>`
    );

    const controls = [
      spectator.query('input', { root: true }),
      spectator.query('select', { root: true }),
      spectator.query('textarea', { root: true })
    ];
    expect(controls.map(control => control?.hasAttribute('required'))).toEqual([true, true, true]);
  });

  it('an optional field leaves its control without the attribute', () => {
    spectator = createHost(`<app-form-field label="Remark"><app-input /></app-form-field>`);

    expect(spectator.query('input')?.hasAttribute('required')).toBe(false);
  });

  it('keeps the marker next to a hint and an error (the other states of the field)', () => {
    const control = new FormControl('', [Validators.required]);
    control.markAsTouched();
    spectator = createHost(
      `<app-form-field label="Name" hint="Shown on the receipt" [required]="true" [control]="control">
        <app-input />
      </app-form-field>`,
      { hostProps: { control } }
    );

    expect(accessibleLabel(label())).toBe('Name');
    expect(spectator.query<HTMLInputElement>('input')?.required).toBe(true);
    expect(spectator.query('.ui-form-field-hint')?.textContent).toContain('Shown on the receipt');
    expect(spectator.query('.ui-form-field-error')).not.toBeNull();
    expect(spectator.query('input')?.getAttribute('aria-invalid')).toBe('true');
  });
});

describe('UiInputComponent outside a form field', () => {
  const createHost = createHostFactory({ component: UiInputComponent });

  it('has no required attribute', () => {
    const spectator = createHost(`<app-input />`);

    expect(spectator.query('input')?.hasAttribute('required')).toBe(false);
  });
});
