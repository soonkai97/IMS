import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';

/**
 * D4 / RQ-01 / RQ-03 / RQ-04 / RQ-05, checked on the sources themselves so a new form cannot forget the marker or the
 * legend and nobody brings back the "(required)" text:
 *  - a control that has `Validators.required` in its form definition sits in a field marked `[required]`;
 *  - a form with a required field places `<app-form-legend />` exactly once, a form with no required field none;
 *  - the asterisk and the legend mark use the danger colour token.
 */
const LIBS = resolve(__dirname, '../../../../libs');
const OLD_REQUIRED_TEXT = /\((required|wajib)\)|[(（]必填[)）]/;
const REQUIRED_BINDING = /\[required\]="(?!false")/;
const LEGEND = /<app-form-legend\b/g;
const FORM_BLOCK = /<form\b[\s\S]*?<\/form>/g;
const FIELD_BLOCK = /<app-form-field\b[\s\S]*?<\/app-form-field>/g;
const GROUP_BLOCK = /<app-radio-group\b[^>]*?\/>/g;

/** Where the controls of a template are defined when that is not a `.ts` file next to it. */
const EXTRA_CONTROL_SOURCES: Record<string, string[]> = {
  'containers/documents/src/lib/line-editor/line-editor.component.html': [
    'features/documents/src/lib/lines/document-lines.ts'
  ]
};
/**
 * Documented exception: the line editor is a part of the document editor page, whose header form already carries the
 * one legend of the page, so a second one would break "once per form".
 */
const LEGEND_HOSTED_ELSEWHERE = new Set([
  'containers/documents/src/lib/line-editor/line-editor.component.html'
]);

function filesUnder(directory: string, extension: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return entry.name === 'node_modules' ? [] : filesUnder(path, extension);
    }
    return path.endsWith(extension) ? [path] : [];
  });
}

const relative = (path: string): string => path.slice(LIBS.length + 1).replace(/\\/g, '/');

/** Names of the form controls whose definition (up to the next control or the end of the statement) has Validators.required. */
function requiredControlNames(source: string): Set<string> {
  const definitions = [...source.matchAll(/(\w+):\s*new FormControl\b/g)];
  const names = new Set<string>();
  definitions.forEach((definition, index) => {
    const start = definition.index ?? 0;
    const next = definitions[index + 1]?.index ?? source.length;
    const statementEnd = source.indexOf(';', start);
    const end = Math.min(next, statementEnd === -1 ? source.length : statementEnd);
    if (source.slice(start, end).includes('Validators.required')) {
      names.add(definition[1]);
    }
  });
  return names;
}

function controlNamesOf(block: string): string[] {
  return [
    ...[...block.matchAll(/controls\.(\w+)/g)].map(match => match[1]),
    ...[...block.matchAll(/formControlName="(\w+)"/g)].map(match => match[1])
  ];
}

const templates = filesUnder(LIBS, '.html').map(path => ({
  path,
  name: relative(path),
  html: readFileSync(path, 'utf8')
}));

function requiredNamesFor(template: { path: string; name: string }): Set<string> {
  const sources = [
    ...readdirSync(dirname(template.path))
      .filter(file => file.endsWith('.ts') && !file.endsWith('.spec.ts'))
      .map(file => join(dirname(template.path), file)),
    ...(EXTRA_CONTROL_SOURCES[template.name] ?? []).map(file => join(LIBS, file))
  ];
  return new Set(sources.flatMap(file => [...requiredControlNames(readFileSync(file, 'utf8'))]));
}

describe('required marker and legend in every template (D4)', () => {
  it('finds the templates (guards against a wrong path)', () => {
    expect(templates.length).toBeGreaterThan(40);
  });

  it('the control scan sees Validators.required (guards against a regex that matches nothing)', () => {
    const product = templates.find(({ name }) => name.endsWith('product-edit.component.html'));
    expect([
      ...requiredControlNames(
        readFileSync(join(dirname(product?.path ?? ''), 'product-edit.component.ts'), 'utf8')
      )
    ]).toEqual(
      expect.arrayContaining(['sku', 'name', 'baseUomId', 'safetyStock', 'fromUomId', 'factor'])
    );
  });

  it.each(templates.filter(({ html }) => /<app-(form-field|radio-group)\b/.test(html)))(
    '$name: a control with Validators.required sits in a field marked [required]',
    template => {
      const required = requiredNamesFor(template);
      const unmarked = [
        ...(template.html.match(FIELD_BLOCK) ?? []),
        ...(template.html.match(GROUP_BLOCK) ?? [])
      ]
        .filter(block => controlNamesOf(block).some(name => required.has(name)))
        .filter(block => !REQUIRED_BINDING.test(block))
        .map(block => controlNamesOf(block).join(','));

      expect(unmarked).toEqual([]);
    }
  );

  it.each(templates.filter(({ html }) => (html.match(FORM_BLOCK) ?? []).length > 0))(
    '$name: each form places the legend once when it has a required field, never otherwise',
    ({ html }) => {
      for (const form of html.match(FORM_BLOCK) ?? []) {
        const legends = (form.match(LEGEND) ?? []).length;
        expect({ legends }).toEqual({ legends: REQUIRED_BINDING.test(form) ? 1 : 0 });
      }
    }
  );

  it.each(
    templates.filter(
      ({ html, name }) => REQUIRED_BINDING.test(html) && !LEGEND_HOSTED_ELSEWHERE.has(name)
    )
  )('$name: a template with a required field has a legend', ({ html }) => {
    expect((html.match(LEGEND) ?? []).length).toBeGreaterThanOrEqual(1);
  });

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

describe('the asterisk is drawn in the danger colour (RQ-04, token only, no literal colour)', () => {
  const ui = join(LIBS, 'shared/ui/src/lib/components');
  const rules: [string, string][] = [
    ['form-field/form-field.component.scss', '.ui-form-field-asterisk'],
    ['radio-group/radio-group.component.scss', '.ui-radio-group-asterisk'],
    ['form-legend/form-legend.component.scss', '.ui-form-legend-mark']
  ];

  it.each(rules)('%s: %s uses color("danger")', (file, selector) => {
    const scss = readFileSync(join(ui, file), 'utf8');
    const start = scss.indexOf(`${selector} {`);
    expect(start).toBeGreaterThanOrEqual(0);
    const body = scss.slice(start, scss.indexOf('}', start));
    expect(body).toMatch(/color:\s*color\('danger'\)/);
  });
});
