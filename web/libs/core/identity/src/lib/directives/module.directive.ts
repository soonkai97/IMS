import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { ModulesStore } from '../services/modules.store';

/**
 * Renders its template only while the module is on:
 *
 *     <app-input *appModule="'batch'" ... />
 *
 * The same signal as the menu and the route guard, so a field of a module goes with it. Combine with `*appCan` on a wrapper element when a
 * permission applies too (a structural directive per element). The element is not rendered at all, so no placeholder is left behind.
 */
@Directive({ selector: '[appModule]' })
export class ModuleDirective {
  private readonly modules = inject(ModulesStore);
  private readonly template = inject<TemplateRef<unknown>>(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private rendered = false;

  $module = input.required<string>({ alias: 'appModule' });

  constructor() {
    effect(() => {
      const on = this.modules.isEnabled(this.$module());
      if (on && !this.rendered) {
        this.container.createEmbeddedView(this.template);
        this.rendered = true;
      } else if (!on && this.rendered) {
        this.container.clear();
        this.rendered = false;
      }
    });
  }
}
