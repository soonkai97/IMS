import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { createHostFactory, SpectatorHost } from '@ngneat/spectator/jest';
import { UiFormFieldComponent } from '../form-field/form-field.component';
import { UiDateFieldComponent } from './date-field.component';

describe('UiDateFieldComponent (R23)', () => {
  let spectator: SpectatorHost<UiFormFieldComponent, { control: FormControl<string> }>;
  const createHost = createHostFactory({
    component: UiFormFieldComponent,
    imports: [ReactiveFormsModule, UiDateFieldComponent]
  });

  function render(value = ''): FormControl<string> {
    const control = new FormControl(value, { nonNullable: true });
    spectator = createHost(
      `<app-form-field label="From" [control]="control">
        <app-date-field [formControl]="control" />
      </app-form-field>`,
      { hostProps: { control } }
    );
    return control;
  }

  const text = () => spectator.query('input[type="text"]') as HTMLInputElement;
  const picker = () => spectator.query('input[type="date"]') as HTMLInputElement;
  const button = () => spectator.query('button') as HTMLButtonElement;
  const error = () => spectator.query('.ui-form-field-error')?.textContent?.trim() ?? '';

  function type(value: string): void {
    spectator.typeInElement(value, text());
    spectator.detectChanges();
  }

  function leave(): void {
    spectator.dispatchFakeEvent(text(), 'blur');
    spectator.detectChanges();
  }

  it('shows an ISO value in the app format dd/MM/yyyy', () => {
    render('2026-10-09');

    expect(text().value).toBe('09/10/2026');
  });

  it('shows an empty value as the pattern, never as mm/dd/yyyy', () => {
    render();

    expect(text().value).toBe('');
    expect(text().placeholder).toBe('dd/mm/yyyy');
  });

  it('writes a typed app-format date to the form as an ISO day, as soon as it reads', () => {
    const control = render();

    type('09 Oct 2026');

    expect(control.value).toBe('2026-10-09');
    expect(control.valid).toBe(true);
  });

  it('also reads a typed ISO day, a typed month name and a typed dd/MM/yyyy day, and rewrites them on blur', () => {
    const control = render();

    type('2026-10-09');
    leave();
    expect(control.value).toBe('2026-10-09');
    expect(text().value).toBe('09/10/2026');

    type('09 Nov 2026');
    leave();
    expect(control.value).toBe('2026-11-09');
    expect(text().value).toBe('09/11/2026');

    type('10/11/2026');
    leave();
    expect(control.value).toBe('2026-11-10');
    expect(text().value).toBe('10/11/2026');
  });

  it('applies nothing while the text is half typed, so a filter does not run on 09 Oct 20', () => {
    const control = render('2026-10-01');
    const changes: string[] = [];
    control.valueChanges.subscribe(value => changes.push(value));

    type('09 Oct 20');

    expect(changes).toEqual([]);
    expect(control.value).toBe('2026-10-01');
  });

  it('types an ISO day through without applying a wrong one on the way (2026-10-1 is not 1 October)', () => {
    const control = render();
    const changes: string[] = [];
    control.valueChanges.subscribe(value => changes.push(value));

    for (const typed of ['2026-1', '2026-10-', '2026-10-1', '2026-10-15']) {
      type(typed);
    }

    expect(changes).toEqual(['2026-10-15']);
  });

  it('on blur, text that is no date clears the value and says what to type', () => {
    const control = render('2026-10-01');

    type('31 Feb 2026');
    leave();

    expect(control.value).toBe('');
    expect(control.errors).toEqual({ date: true });
    expect(error()).toContain('Invalid value (date).');
    expect(text().value).toBe('31 Feb 2026');
    expect(text().getAttribute('aria-invalid')).toBe('true');
    expect(text().getAttribute('aria-describedby')).toBe(
      spectator.query('.ui-form-field-error')?.id
    );
  });

  it('an invalid text is fixed by typing a date: the error goes', () => {
    const control = render();
    type('nope');
    leave();

    type('09 Oct 2026');
    leave();

    expect(control.errors).toBeNull();
    expect(error()).toBe('');
  });

  it('emptying the text clears the value without an error', () => {
    const control = render('2026-10-09');

    type('');
    leave();

    expect(control.value).toBe('');
    expect(control.errors).toBeNull();
  });

  it('a value set from outside (Clear filters) replaces the text and any error', () => {
    const control = render();
    type('nope');
    leave();

    control.setValue('');
    spectator.detectChanges();

    expect(text().value).toBe('');
    expect(control.errors).toBeNull();
  });

  it('reads the app format from the form again after a value is set from outside', () => {
    const control = render();

    control.setValue('2026-01-05');
    spectator.detectChanges();

    expect(text().value).toBe('05/01/2026');
  });

  it('keeps the field labelled by its label and the calendar button named after the field (EA-29)', () => {
    render();

    const label = spectator.query('label') as HTMLLabelElement;
    expect(label.htmlFor).toBe(text().id);
    expect(button().getAttribute('aria-label')).toBe('Choose date');
    expect(button().getAttribute('aria-labelledby')).toBe(`${button().id} ${label.id}`);
    expect(button().type).toBe('button');
  });

  it('keeps the browser picker out of the tab order and away from screen readers', () => {
    render();

    expect(picker().tabIndex).toBe(-1);
    expect(picker().getAttribute('aria-hidden')).toBe('true');
    expect(picker().getAttribute('lang')).toBe('en-GB');
    expect(picker().getAttribute('min')).toBe('2000-01-01');
  });

  it('the calendar button opens the browser picker on the current day', () => {
    render('2026-10-09');
    const showPicker = jest.fn();
    picker().showPicker = showPicker;

    spectator.click(button());

    expect(showPicker).toHaveBeenCalledTimes(1);
    expect(picker().value).toBe('2026-10-09');
  });

  it('survives a browser without showPicker, or one that refuses it', () => {
    render();

    expect(() => spectator.click(button())).not.toThrow();
    picker().showPicker = () => {
      throw new Error('NotAllowedError');
    };
    expect(() => spectator.click(button())).not.toThrow();
  });

  it('a day chosen in the browser picker is written as an ISO day and shown in the app format', () => {
    const control = render();
    type('nope');
    leave();

    picker().value = '2026-10-09';
    spectator.dispatchFakeEvent(picker(), 'change');
    spectator.detectChanges();

    expect(control.value).toBe('2026-10-09');
    expect(control.errors).toBeNull();
    expect(text().value).toBe('09/10/2026');
    expect(control.touched).toBe(true);
  });

  it('is disabled with its form control', () => {
    const control = render();

    control.disable();
    spectator.detectChanges();

    expect(text().disabled).toBe(true);
    expect(button().disabled).toBe(true);
  });
});
