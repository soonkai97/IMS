import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { StatusTone, UiStatusChipComponent } from './status-chip.component';

/** Spectator types `props` by property name ($label) but Angular binds the alias (label): cast at the boundary. */
describe('UiStatusChipComponent', () => {
  let spectator: Spectator<UiStatusChipComponent>;
  const createComponent = createComponentFactory({
    component: UiStatusChipComponent,
    detectChanges: false
  });

  it.each<[StatusTone, string]>([
    ['draft', 'pencil'],
    ['submitted', 'clock'],
    ['approved', 'check'],
    ['posted', 'check-double'],
    ['reversed', 'undo'],
    ['cancelled', 'x']
  ])('%s chip shows its colour class, an icon and the text label', (status, icon) => {
    spectator = createComponent({ props: { label: `Label ${status}`, status } as never });
    spectator.detectChanges();

    expect(spectator.element.classList).toContain(`--${status}`);
    expect(spectator.query('svg')).toBeTruthy();
    expect(spectator.component.$icon()).toBe(icon);
    expect(spectator.element.textContent).toContain(`Label ${status}`);
  });

  it('L00: a long label keeps its full text in the title while the chip cuts it with an ellipsis', () => {
    const label = 'Z'.repeat(200);
    spectator = createComponent({ props: { label, status: 'draft' } as never });
    spectator.detectChanges();

    const text = spectator.query('.ui-status-chip-label');
    expect(text?.textContent).toBe(label);
    expect(text?.getAttribute('title')).toBe(label);
  });

  it('flag chips are outlined and carry the text too (never colour alone)', () => {
    spectator = createComponent({ props: { label: 'Returned', flag: 'warning' } as never });
    spectator.detectChanges();

    expect(spectator.element.classList).toContain('--flag-warning');
    expect(spectator.element.textContent).toContain('Returned');
  });
});
