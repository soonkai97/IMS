import { DocumentDto, DocumentStatus, DocumentType, IssuePurpose } from '@ims/api';
import { canReturn } from './can-return';

function document(over: Partial<DocumentDto> = {}): DocumentDto {
  return {
    type: DocumentType.Issue,
    purpose: IssuePurpose.Sale,
    status: DocumentStatus.Posted,
    lines: [{ qty: 10, returnedQty: 6, returnableQty: 4 }],
    ...over
  } as unknown as DocumentDto;
}

describe('canReturn', () => {
  it('a Posted sale issue or receipt with something left to return, for a holder of returns.create', () => {
    expect(canReturn(document(), true)).toBe(true);
    expect(canReturn(document({ type: DocumentType.Receipt, purpose: undefined }), true)).toBe(
      true
    );
  });

  it.each([
    ['no returns.create', document(), false],
    ['no document yet', undefined, true],
    ['not Posted', document({ status: DocumentStatus.Approved }), true],
    ['a return itself', document({ returnOf: { id: 'x', no: 'ISS-1' } }), true],
    ['an issue for use', document({ purpose: IssuePurpose.Use }), true],
    ['a supplier return (purpose Return)', document({ purpose: IssuePurpose.Return }), true],
    ['a transfer', document({ type: DocumentType.Transfer }), true],
    [
      'figures the API leaves out (module off, or no right)',
      document({ lines: [{ qty: 10 }] as never }),
      true
    ],
    [
      'nothing left',
      document({ lines: [{ qty: 10, returnedQty: 10, returnableQty: 0 }] as never }),
      true
    ]
  ])('is not offered for %s', (_label, doc, mayCreate) => {
    expect(canReturn(doc, mayCreate)).toBe(false);
  });
});
