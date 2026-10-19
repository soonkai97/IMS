import { TestBed } from '@angular/core/testing';
import { I18nService } from './i18n.service';

describe('I18nService.roleLabel (display only)', () => {
  let i18n: I18nService;

  beforeEach(async () => {
    i18n = TestBed.inject(I18nService);
    await i18n.setLanguage('en');
  });

  it('shows the name the API gave for a role it has no translation for', () => {
    expect(i18n.roleLabel('QARoleA', 'QA role A')).toBe('QA role A');
  });

  it('shows a standard role in the language of the person, like the header does', async () => {
    expect(i18n.roleLabel('Clerk', 'Clerk')).toBe('Clerk');

    await i18n.setLanguage('zh');

    expect(i18n.roleLabel('Clerk', 'Clerk')).toBe(i18n.t('role.Clerk'));
    expect(i18n.roleLabel('Clerk', 'Clerk')).not.toBe('Clerk');
    expect(i18n.roleLabel('QARoleA', 'QA role A')).toBe('QA role A');
  });
});
