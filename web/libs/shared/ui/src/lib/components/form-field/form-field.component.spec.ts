import { FormControl, Validators } from '@angular/forms';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
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

  it('falls back to the kit sentence for errors it has no field wording for', () => {
    render(new FormControl(null, [Validators.required]), { min: 'Enter a quantity above 0.' });

    expect(spectator.query('.ui-form-field-error')?.textContent).toContain(
      'Invalid value (required)'
    );
  });
});
