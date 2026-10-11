import { createComponentFactory } from '@ngneat/spectator/jest';
import { UI_TEXT, UiText } from '../../tokens/ui-text.token';
import { UiFormLegendComponent } from './form-legend.component';

function withLegend(requiredLegend: string) {
  return createComponentFactory({
    component: UiFormLegendComponent,
    providers: [{ provide: UI_TEXT, useValue: () => ({ requiredLegend }) as UiText }]
  });
}

describe('UiFormLegendComponent (D4, RQ-03)', () => {
  const createComponent = createComponentFactory({ component: UiFormLegendComponent });

  it('says "* means required" with the asterisk in its own span', () => {
    const spectator = createComponent();

    expect(spectator.element.textContent?.replace(/\s+/g, ' ').trim()).toBe('* means required');
    expect(spectator.query('.ui-form-legend-mark')?.textContent).toBe('*');
  });

  it('is hidden from assistive technology: every required control already says "required"', () => {
    const spectator = createComponent();

    expect(spectator.element.getAttribute('aria-hidden')).toBe('true');
  });
});

describe('UiFormLegendComponent in another language', () => {
  const createComponent = withLegend('* 表示必填');

  it('shows the translated sentence and still draws the asterisk in its own span', () => {
    const spectator = createComponent();

    expect(spectator.element.textContent?.replace(/\s+/g, ' ').trim()).toBe('* 表示必填');
    expect(spectator.query('.ui-form-legend-mark')?.textContent).toBe('*');
  });
});

describe('UiFormLegendComponent with a sentence that has no leading asterisk', () => {
  const createComponent = withLegend('Required');

  it('shows the sentence whole', () => {
    const spectator = createComponent();

    expect(spectator.element.textContent?.replace(/\s+/g, ' ').trim()).toBe('Required');
    expect(spectator.query('.ui-form-legend-mark')).toBeNull();
  });
});
