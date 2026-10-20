import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiSwitchComponent } from './switch.component';

describe('UiSwitchComponent', () => {
  let spectator: Spectator<UiSwitchComponent>;
  const createComponent = createComponentFactory({
    component: UiSwitchComponent,
    detectChanges: false
  });

  const control = (): HTMLButtonElement =>
    spectator.query<HTMLButtonElement>('button[role="switch"]') as HTMLButtonElement;

  function open(props: Record<string, unknown> = {}): boolean[] {
    spectator = createComponent({
      props: { labelledBy: 'name', onText: 'On', offText: 'Off', ...props } as never
    });
    const asked: boolean[] = [];
    spectator.output('$toggled').subscribe((value: boolean) => asked.push(value));
    spectator.detectChanges();
    return asked;
  }

  it('is a switch named by its heading, and says its state in words as well as by aria-checked', () => {
    open();

    expect(control().getAttribute('aria-labelledby')).toBe('name');
    expect(control().getAttribute('aria-checked')).toBe('false');
    expect(spectator.query('.ui-switch-text')?.textContent?.trim()).toBe('Off');

    spectator.setInput('checked', true);
    expect(control().getAttribute('aria-checked')).toBe('true');
    expect(spectator.query('.ui-switch-text')?.textContent?.trim()).toBe('On');
  });

  it('asks for the opposite state and does not change by itself', () => {
    const asked = open();

    spectator.click(control());
    expect(asked).toEqual([true]);
    expect(control().getAttribute('aria-checked')).toBe('false');

    spectator.setInput('checked', true);
    spectator.click(control());
    expect(asked).toEqual([true, false]);
  });

  it('is reached by Tab when disabled (aria-disabled, not disabled), asks for nothing and points to the reason', () => {
    const asked = open({ disabled: true, describedBy: 'why' });

    spectator.click(control());

    expect(control().disabled).toBe(false);
    expect(control().getAttribute('aria-disabled')).toBe('true');
    expect(control().getAttribute('aria-describedby')).toBe('why');
    expect(asked).toEqual([]);
  });

  it('ignores a tap while a change is being saved and says it is busy', () => {
    const asked = open({ busy: true });

    spectator.click(control());

    expect(asked).toEqual([]);
    expect(control().getAttribute('aria-busy')).toBe('true');
    expect(control().getAttribute('aria-disabled')).toBe('true');
  });
});
