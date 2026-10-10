import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output
} from '@angular/core';
import { ReactiveFormsModule } from '@angular/forms';
import { DocumentType } from '@ims/api';
import { AppQtyPipe, TranslatePipe } from '@ims/core/i18n';
import {
  LineGroup,
  LineValue,
  LinesArray,
  lineShortfall,
  maxCoveredQuantity
} from '@ims/features/documents';
import {
  SelectOption,
  UiButtonComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiInputComponent,
  UiSelectComponent
} from '@ims/ui';

export interface LineRow {
  readonly index: number;
  readonly group: LineGroup;
  readonly value: LineValue;
  readonly shortfall: number;
  readonly maxCovered: number;
  readonly unitOptions: readonly SelectOption[];
  readonly hasUnitChoice: boolean;
  readonly unitCode: string;
  /** True when the stock does not cover the line and negative stock is not allowed (blocks submit). */
  readonly blocked: boolean;
  /** True when the stock does not cover the line but a supervisor may override. */
  readonly needsOverride: boolean;
}

/**
 * Editable list of document lines: quantity with +/- steppers, unit, cost (receipts), remark. For issues it shows
 * the on-hand quantity and the shortfall messages E1 / E2 (docs/design/mvp-ux.md 3.2). Rows are cards on phones.
 */
@Component({
  selector: 'app-document-line-editor',
  imports: [
    AppQtyPipe,
    ReactiveFormsModule,
    TranslatePipe,
    UiButtonComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiInputComponent,
    UiSelectComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './line-editor.component.html',
  styleUrl: './line-editor.component.scss'
})
export class DocumentLineEditorComponent {
  $lines = input.required<LinesArray>({ alias: 'lines' });
  /** Current values of all lines (the editor's single source for derived numbers). */
  $values = input.required<readonly LineValue[]>({ alias: 'values' });
  $type = input.required<DocumentType>({ alias: 'type' });
  $warehouseName = input('', { alias: 'warehouseName' });
  $allowNegativeStock = input(false, { alias: 'allowNegativeStock', transform: booleanAttribute });
  $readonly = input(false, { alias: 'readonly', transform: booleanAttribute });

  $stepped = output<{ group: LineGroup; delta: number }>({ alias: 'stepped' });
  $removed = output<number>({ alias: 'removed' });
  $setToAvailable = output<LineGroup>({ alias: 'setToAvailable' });

  $isReceipt = computed(() => this.$type() === DocumentType.Receipt);
  $isIssue = computed(() => this.$type() === DocumentType.Issue);
  $rows = computed<readonly LineRow[]>(() => {
    const groups = this.$lines().controls;
    const allowNegative = this.$allowNegativeStock();
    return this.$values().map((value, index) => {
      const shortfall = this.$isIssue() ? lineShortfall(value) : 0;
      return {
        index,
        group: groups[index],
        value,
        shortfall,
        maxCovered: maxCoveredQuantity(value),
        unitOptions: value.uomOptions.map(option => ({ value: option.uomId, label: option.code })),
        hasUnitChoice: value.uomOptions.length > 1,
        unitCode:
          value.uomOptions.find(option => option.uomId === value.uomId)?.code ?? value.baseUomCode,
        blocked: shortfall > 0 && !allowNegative,
        needsOverride: shortfall > 0 && allowNegative
      };
    });
  });

  onStep(group: LineGroup, delta: number): void {
    this.$stepped.emit({ group, delta });
  }

  onRemove(index: number): void {
    this.$removed.emit(index);
  }

  onSetToAvailable(group: LineGroup): void {
    this.$setToAvailable.emit(group);
  }
}
