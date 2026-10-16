import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it('keeps a path inside the app, with its query and fragment', () => {
    for (const url of ['/receipts', '/receipts/new', '/stock?warehouse=2#top', '/a%20b']) {
      expect(safeReturnUrl(url)).toBe(url);
    }
  });

  it.each([
    undefined,
    '',
    'receipts',
    '//evil.com',
    '/\\evil.com',
    'https://evil.com',
    'javascript:alert(1)',
    '/%5Cevil.com',
    '/login',
    '/login?returnUrl=/x'
  ])('sends %p Home', url => {
    expect(safeReturnUrl(url)).toBe('/');
  });
});
