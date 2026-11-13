import { FormArray } from '@angular/forms';
import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DocumentType } from '@ims/api';
import { LineValue, LinesArray, createLineGroup } from '@ims/features/documents';
import { DocumentLineEditorComponent } from './line-editor.component';

const UOM_ID = '00000000-0000-4000-8000-000000000001';

const value = (sku: string): LineValue => ({
  productId: '00000000-0000-4000-8000-000000000002',
  sku,
  productName: `Product ${sku}`,
  qty: 2,
  uomId: UOM_ID,
  unitCost: null,
  remark: '',
  available: null,
  baseUomCode: 'PCS',
  uomOptions: [{ uomId: UOM_ID, code: 'PCS', factorToBase: 1 }],
  purchaseOrderLineNo: null,
  salesOrderLineNo: null,
  returnOfLineNo: null
});

describe('DocumentLineEditorComponent rows', () => {
  let spectator: Spectator<DocumentLineEditorComponent>;
  const createComponent = createComponentFactory({
    component: DocumentLineEditorComponent,
    detectChanges: false
  });

  function render(values: LineValue[], groups: LineValue[]): void {
    const lines: LinesArray = new FormArray(groups.map(createLineGroup));
    spectator = createComponent({
      props: { lines, values, type: DocumentType.Receipt } as never
    });
    spectator.detectChanges();
  }

  it('draws one row for each line, in order', () => {
    const lines = [value('A-1'), value('B-2')];
    render(lines, lines);

    expect(spectator.component.$rows().map(row => row.value.sku)).toEqual(['A-1', 'B-2']);
    expect(spectator.component.$rows().map(row => row.index)).toEqual([0, 1]);
  });

  it('draws no row for a value that has no control group (the two lists are one line each), and keeps the others at their index', () => {
    const lines = [value('A-1'), value('B-2')];
    render(lines, lines.slice(0, 1));

    expect(spectator.component.$rows().map(row => row.value.sku)).toEqual(['A-1']);
  });
});
