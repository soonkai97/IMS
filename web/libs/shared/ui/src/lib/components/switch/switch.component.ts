import { ChangeDetectionStrategy, Component, booleanAttribute, input, output } from '@angular/core';

/**
 * On/off switch for a setting that does not change by itself: it shows `checked` and asks for the opposite with `toggled`; the
 * screen decides (it may first ask for a reason) and passes the new `checked` back. The state is in words ("On"/"Off") as well as in the
 * track, never in colour alone, and a screen reader hears role switch, the state and the name of the element `labelledBy`.
 * `disabled` keeps the control reachable by Tab (aria-disabled), so the sentence `describedBy` points to is read with it.
 */
@Component({
  selector: 'app-switch',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './switch.component.html',
  styleUrl: './switch.component.scss'
})
export class UiSwitchComponent {
  $checked = input(false, { alias: 'checked', transform: booleanAttribute });
  /** Id of the element that names the setting (its heading). The visible On/Off text is the state, not the name. */
  $labelledBy = input.required<string>({ alias: 'labelledBy' });
  $describedBy = input<string | undefined>(undefined, { alias: 'describedBy' });
  $onText = input.required<string>({ alias: 'onText' });
  $offText = input.required<string>({ alias: 'offText' });
  $disabled = input(false, { alias: 'disabled', transform: booleanAttribute });
  /** A change is being saved: the control ignores taps and is announced as busy, but keeps the focus. */
  $busy = input(false, { alias: 'busy', transform: booleanAttribute });

  /** The state the user asks for (the opposite of `checked`). */
  $toggled = output<boolean>({ alias: 'toggled' });

  onClick(): void {
    if (this.$disabled() || this.$busy()) {
      return;
    }
    this.$toggled.emit(!this.$checked());
  }
}
