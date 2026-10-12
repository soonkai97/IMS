import { Directive, computed, input } from '@angular/core';
import { tabButtonId } from './tabs.component';

/**
 * Marks the element that `app-tabs` controls: `<app-tabs panel="x" [active]="tab" />` and
 * `<div appTabPanel="x" [labelledByTab]="tab">` (the same id, and the id of the selected tab).
 */
@Directive({
  selector: '[appTabPanel]',
  host: {
    role: 'tabpanel',
    '[id]': '$panel()',
    '[attr.aria-labelledby]': '$labelledBy()'
  }
})
export class UiTabPanelDirective {
  $panel = input.required<string>({ alias: 'appTabPanel' });
  $tab = input.required<string>({ alias: 'labelledByTab' });
  $labelledBy = computed(() => tabButtonId(this.$tab()));
}
