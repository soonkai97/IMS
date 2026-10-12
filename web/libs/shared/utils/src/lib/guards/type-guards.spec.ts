import { isPublicId } from './type-guards';

describe('isPublicId', () => {
  it('accepts the text form of a GUID, in either case', () => {
    expect(isPublicId('3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b')).toBe(true);
    expect(isPublicId('3F2B8C1E-5A4D-4E6F-9B7A-1C2D3E4F5A6B')).toBe(true);
  });

  it('rejects an old numeric id, an empty value and anything that is not a GUID', () => {
    expect(isPublicId('31')).toBe(false);
    expect(isPublicId(31)).toBe(false);
    expect(isPublicId('')).toBe(false);
    expect(isPublicId(undefined)).toBe(false);
    expect(isPublicId(null)).toBe(false);
    expect(isPublicId('new')).toBe(false);
    expect(isPublicId('3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6')).toBe(false);
    expect(isPublicId(' 3f2b8c1e-5a4d-4e6f-9b7a-1c2d3e4f5a6b')).toBe(false);
  });
});
