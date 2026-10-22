import { IssuePurpose } from '@ims/api';
import { MAX_DOCUMENT_LINES } from '@ims/models';
import { parseEditorDraft } from './document-editor.draft';

const guid = (n: number): string => `00000000-0000-4000-8000-${String(n).padStart(12, '0')}`;
const lowerGuid = 'abcdef00-0000-4000-8000-000000000009';

const line = (overrides: Record<string, unknown> = {}) => ({
  productId: guid(1),
  sku: 'DEMO-001',
  productName: 'Demo widget',
  qty: 3,
  uomId: guid(2),
  unitCost: null,
  remark: '',
  available: 10,
  baseUomCode: 'PCS',
  uomOptions: [{ uomId: guid(2), code: 'PCS', factorToBase: 1 }],
  purchaseOrderLineNo: null,
  ...overrides
});

const header = (overrides: Record<string, unknown> = {}) => ({
  warehouseId: guid(3),
  partnerId: null,
  purpose: IssuePurpose.Sale,
  remark: 'r',
  ...overrides
});

const draft = (overrides: Record<string, unknown> = {}) => ({
  version: 1,
  header: header(),
  lines: [line()],
  basedOn: null,
  ...overrides
});

const lines = (count: number) => Array.from({ length: count }, () => line());

describe('parseEditorDraft (what comes back from storage is untrusted)', () => {
  it('accepts a draft the editor wrote, as an equal copy', () => {
    expect(parseEditorDraft(draft())).toEqual(draft());
  });

  it('accepts exactly the line limit and refuses one more (3000 lines froze the page for 28 s)', () => {
    expect(parseEditorDraft(draft({ lines: lines(MAX_DOCUMENT_LINES) }))).toBeDefined();
    expect(parseEditorDraft(draft({ lines: lines(MAX_DOCUMENT_LINES + 1) }))).toBeUndefined();
  });

  it('writes ids in the API lower case (decision 16)', () => {
    const parsed = parseEditorDraft(
      draft({
        header: header({ warehouseId: lowerGuid.toUpperCase() }),
        lines: [line({ productId: lowerGuid.toUpperCase() })]
      })
    );

    expect(parsed?.header.warehouseId).toBe(lowerGuid);
    expect(parsed?.lines[0].productId).toBe(lowerGuid);
  });

  it.each(['zzz', '123', '', 5, {}])('refuses the id %p', bad => {
    expect(parseEditorDraft(draft({ header: header({ warehouseId: bad }) }))).toBeUndefined();
    expect(parseEditorDraft(draft({ header: header({ partnerId: bad }) }))).toBeUndefined();
    expect(parseEditorDraft(draft({ lines: [line({ productId: bad })] }))).toBeUndefined();
    expect(parseEditorDraft(draft({ lines: [line({ uomId: bad })] }))).toBeUndefined();
  });

  it.each<[string, unknown]>([
    ['not an object', 'boom'],
    ['null', null],
    ['lines that are a string', draft({ lines: 'boom' })],
    ['no header', draft({ header: undefined })],
    ['no version', draft({ version: undefined })],
    ['another version', draft({ version: 2 })],
    [
      'a remark over the limit (4 MB was restored)',
      draft({ header: header({ remark: 'x'.repeat(501) }) })
    ],
    ['an unknown purpose', draft({ header: header({ purpose: 'Gift' }) })],
    ['a base version that is not text', draft({ basedOn: 5 })],
    ['a base version of 4 MB', draft({ basedOn: 'x'.repeat(4_000_000) })],
    ['a line that is not an object', draft({ lines: [5] })],
    ['a quantity that is text', draft({ lines: [line({ qty: '3' })] })],
    ['a negative quantity', draft({ lines: [line({ qty: -1 })] })],
    ['a quantity over the limit', draft({ lines: [line({ qty: 2e9 })] })],
    ['a quantity that is not finite', draft({ lines: [line({ qty: Infinity })] })],
    ['a cost over the limit', draft({ lines: [line({ unitCost: 2e9 })] })],
    ['a line remark over the limit', draft({ lines: [line({ remark: 'x'.repeat(501) })] })],
    ['a product name over the limit', draft({ lines: [line({ productName: 'x'.repeat(301) })] })],
    ['units that are not a list', draft({ lines: [line({ uomOptions: 'boom' })] })],
    [
      'a unit with a factor of zero',
      draft({ lines: [line({ uomOptions: [{ uomId: guid(2), code: 'PCS', factorToBase: 0 }] })] })
    ],
    [
      'a unit with a bad id',
      draft({ lines: [line({ uomOptions: [{ uomId: 'x', code: 'PCS', factorToBase: 1 }] })] })
    ]
  ])('refuses %s', (_name, raw) => {
    expect(parseEditorDraft(raw)).toBeUndefined();
  });

  it('copies only the known fields, so extra properties from storage never reach the form', () => {
    const parsed = parseEditorDraft(
      draft({ lines: [line({ evil: '<img src=x onerror=alert(1)>' })], extra: 1 })
    );

    expect(parsed?.lines[0]).not.toHaveProperty('evil');
    expect(parsed).not.toHaveProperty('extra');
  });
});
