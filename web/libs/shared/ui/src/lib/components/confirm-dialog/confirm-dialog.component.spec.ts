import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiConfirmDialogComponent } from './confirm-dialog.component';

describe('UiConfirmDialogComponent reason box (EA-06)', () => {
  let spectator: Spectator<UiConfirmDialogComponent>;
  const createComponent = createComponentFactory({
    component: UiConfirmDialogComponent,
    detectChanges: false
  });

  beforeEach(() => {
    spectator = createComponent({
      props: {
        open: true,
        heading: 'Reject RCV-1?',
        confirmLabel: 'Reject',
        cancelLabel: 'Back',
        reasonLabel: 'Reason (at least 5 characters)',
        reasonRequired: true,
        minReasonLength: 5
      } as never
    });
    spectator.detectChanges();
  });

  const confirmButton = (): HTMLElement =>
    spectator
      .queryAll('app-button button')
      .find(button => button.textContent?.includes('Reject')) as HTMLElement;

  it('an empty reason says what a reason must be, not just "required"', () => {
    spectator.click(confirmButton());
    spectator.detectChanges();

    expect(spectator.query('.ui-form-field-error')?.textContent).toContain(
      'Enter a reason of at least 5 characters.'
    );
  });

  it('a too short reason gets the same sentence and nothing is confirmed', () => {
    const confirmed: string[] = [];
    spectator.output('$confirmed').subscribe(reason => confirmed.push(reason as string));

    spectator.typeInElement('abc', 'textarea');
    spectator.click(confirmButton());
    spectator.detectChanges();

    expect(spectator.query('.ui-form-field-error')?.textContent).toContain('at least 5 characters');
    expect(confirmed).toEqual([]);
  });

  it('spaces do not count: the API trims a reason before measuring it', () => {
    const confirmed: string[] = [];
    spectator.output('$confirmed').subscribe(reason => confirmed.push(reason as string));

    spectator.typeInElement('   ab   ', 'textarea');
    spectator.click(confirmButton());
    spectator.detectChanges();
    expect(confirmed).toEqual([]);

    spectator.typeInElement('  Wrong lot  ', 'textarea');
    spectator.click(confirmButton());
    expect(confirmed).toEqual(['Wrong lot']);
  });

  it('after a refused confirm the focus goes back to the reason box', async () => {
    spectator.click(confirmButton());
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(document.activeElement?.tagName).toBe('TEXTAREA');
  });

  it('marks the reason box so the dialog focuses it when it opens', () => {
    expect(spectator.query('textarea')?.hasAttribute('data-autofocus')).toBe(true);
  });

  it('D4: a required reason shows the red asterisk and the legend once, above the box', () => {
    expect(spectator.queryAll('app-form-legend')).toHaveLength(1);
    expect(spectator.query('.ui-form-field-asterisk')?.getAttribute('aria-hidden')).toBe('true');
    expect(spectator.query('textarea')?.hasAttribute('required')).toBe(true);
    expect(spectator.query('label')?.textContent).not.toContain('(required)');
  });
});

describe('UiConfirmDialogComponent optional reason (D4)', () => {
  const createComponent = createComponentFactory({
    component: UiConfirmDialogComponent,
    detectChanges: false
  });

  it('an optional reason box has no asterisk and the dialog has no legend', () => {
    const spectator = createComponent({
      props: {
        open: true,
        heading: 'Cancel RCV-1?',
        confirmLabel: 'Cancel document',
        cancelLabel: 'Back',
        reasonLabel: 'Reason (optional)'
      } as never
    });
    spectator.detectChanges();

    expect(spectator.query('textarea')).not.toBeNull();
    expect(spectator.query('app-form-legend')).toBeNull();
    expect(spectator.query('.ui-form-field-asterisk')).toBeNull();
    expect(spectator.query('textarea')?.hasAttribute('required')).toBe(false);
  });
});
