import { Directive, computed, input } from '@angular/core';
import { tabButtonId } from './tabs.component';

/** The element an `app-tabs` strip controls: `<app-tabs panel="x" />` and `<div appTabPanel="x" [labelledByTab]="activeTab">`. */
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
