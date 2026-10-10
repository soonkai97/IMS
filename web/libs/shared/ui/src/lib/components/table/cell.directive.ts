import { Directive, TemplateRef, inject, input } from '@angular/core';

/** Custom cell content:  <ng-template appCell="status" let-row>...</ng-template>  (key = TableColumn.key). */
@Directive({ selector: 'ng-template[appCell]' })
export class UiCellDirective {
  readonly template = inject<TemplateRef<unknown>>(TemplateRef);

  $key = input.required<string>({ alias: 'appCell' });
}
