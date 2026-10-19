import { focusFirstInvalid } from './focus-first-invalid';

describe('focusFirstInvalid', () => {
  function page(html: string): HTMLElement {
    const root = document.createElement('div');
    root.innerHTML = html;
    document.body.append(root);
    return root;
  }

  afterEach(() => (document.body.innerHTML = ''));

  it('focuses the first field marked invalid, not the first field', () => {
    const root = page(
      '<input id="a" /><input id="b" aria-invalid="true" /><input id="c" aria-invalid="true" />'
    );

    expect(focusFirstInvalid(root)).toBe(true);
    expect(document.activeElement?.id).toBe('b');
  });

  it('reaches a radio group marked invalid on its fieldset', () => {
    const root = page(
      '<input id="a" /><fieldset aria-invalid="true"><input id="r1" type="radio" /></fieldset>'
    );

    expect(focusFirstInvalid(root)).toBe(true);
    expect(document.activeElement?.id).toBe('r1');
  });

  it('does nothing when no field is invalid', () => {
    const root = page('<input id="a" />');

    expect(focusFirstInvalid(root)).toBe(false);
    expect(document.activeElement).toBe(document.body);
  });
});
