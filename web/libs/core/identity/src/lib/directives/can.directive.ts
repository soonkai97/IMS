import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';
import { PermissionService } from '../services/permission.service';

/**
 * Renders its template only while the signed-in user holds the permission (or one of the permissions):
 *
 *     <th *appCan="'cost.view'">Unit cost</th>
 *
 * The one place a template asks "may this user see it". Never test a role name: cost and value are decided by the
 * permissions `cost.view` and `valuation.view` (docs/design/mvp-ux.md 1.4). The element is not rendered at all, so no
 * placeholder and no empty box is left behind.
 */
@Directive({ selector: '[appCan]' })
export class CanDirective {
  private readonly permissions = inject(PermissionService);
  private readonly template = inject<TemplateRef<unknown>>(TemplateRef);
  private readonly container = inject(ViewContainerRef);
  private rendered = false;

  $permission = input.required<string | readonly string[]>({ alias: 'appCan' });

  constructor() {
    effect(() => {
      const wanted = this.$permission();
      const list = typeof wanted === 'string' ? [wanted] : wanted;
      const allowed = list.some(permission => this.permissions.has(permission));
      if (allowed && !this.rendered) {
        this.container.createEmbeddedView(this.template);
        this.rendered = true;
      } else if (!allowed && this.rendered) {
        this.container.clear();
        this.rendered = false;
      }
    });
  }
}
