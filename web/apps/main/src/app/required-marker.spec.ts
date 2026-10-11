import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

/**
 * D4 / RQ-03 / RQ-05, checked on the templates themselves so a new form cannot forget the legend and nobody brings
 * back the "(required)" text: a form with a required field places `<app-form-legend />` exactly once, a form with no
 * required field places none.
 */
const LIBS = resolve(__dirname, '../../../../libs');
const OLD_REQUIRED_TEXT = /\((required|wajib)\)|[(（]必填[)）]/;
const REQUIRED_BINDING = /\[required\]="(?!false")/;
const LEGEND = /<app-form-legend\b/g;
const FORM_BLOCK = /<form\b[\s\S]*?<\/form>/g;

function filesUnder(directory: string, extension: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return entry.name === 'node_modules' ? [] : filesUnder(path, extension);
    }
    return path.endsWith(extension) ? [path] : [];
  });
}

const templates = filesUnder(LIBS, '.html').map(path => ({
  name: path.slice(LIBS.length + 1).replace(/\\/g, '/'),
  html: readFileSync(path, 'utf8')
}));

describe('required marker and legend in every template (D4)', () => {
  it('finds the templates (guards against a wrong path)', () => {
    expect(templates.length).toBeGreaterThan(40);
  });

  it.each(templates.filter(({ html }) => (html.match(FORM_BLOCK) ?? []).length > 0))(
    '$name: each form places the legend once when it has a required field, never otherwise',
    ({ html }) => {
      const forms = html.match(FORM_BLOCK) ?? [];
      for (const form of forms) {
        const legends = (form.match(LEGEND) ?? []).length;
        const expected = REQUIRED_BINDING.test(form) ? 1 : 0;
        expect({ requiredField: expected === 1, legends }).toEqual({
          requiredField: expected === 1,
          legends: expected
        });
      }
    }
  );

  it.each(templates.filter(({ html }) => REQUIRED_BINDING.test(html)))(
    '$name: a template with a required field has a legend',
    ({ html }) => {
      expect((html.match(LEGEND) ?? []).length).toBeGreaterThanOrEqual(1);
    }
  );

  it('RQ-05: no template spells "(required)" in a label', () => {
    expect(templates.filter(({ html }) => OLD_REQUIRED_TEXT.test(html)).map(t => t.name)).toEqual(
      []
    );
  });

  it('RQ-05: no catalog spells it either, in English, Chinese or Malay', () => {
    const catalogs = filesUnder(join(LIBS, 'core/i18n/src/lib/messages'), '.messages.ts');
    expect(catalogs).toHaveLength(3);
    for (const catalog of catalogs) {
      expect({ catalog, found: OLD_REQUIRED_TEXT.test(readFileSync(catalog, 'utf8')) }).toEqual({
        catalog,
        found: false
      });
    }
  });
});
