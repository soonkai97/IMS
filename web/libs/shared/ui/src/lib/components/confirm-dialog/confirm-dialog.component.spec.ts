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

  it('after a refused confirm the focus goes back to the reason box', async () => {
    spectator.click(confirmButton());
    spectator.detectChanges();
    await spectator.fixture.whenStable();

    expect(document.activeElement?.tagName).toBe('TEXTAREA');
  });

  it('marks the reason box so the dialog focuses it when it opens', () => {
    expect(spectator.query('textarea')?.hasAttribute('data-autofocus')).toBe(true);
  });
});
