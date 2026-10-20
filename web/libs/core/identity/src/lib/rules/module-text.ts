import { ModuleDto } from '@ims/api';
import { I18nService, isMessageKey } from '@ims/core/i18n';

/**
 * The name and the sentence of a module in the user's language, by code (`module.<code>.name`, `module.<code>.description`). A module added
 * by a newer API has no text yet and shows the API's own English words, like a permission does.
 */
export function moduleName(i18n: I18nService, module: Pick<ModuleDto, 'code' | 'name'>): string {
  const key = `module.${module.code}.name`;
  return isMessageKey(key) ? i18n.t(key) : module.name;
}

export function moduleDescription(
  i18n: I18nService,
  module: Pick<ModuleDto, 'code' | 'description'>
): string {
  const key = `module.${module.code}.description`;
  return isMessageKey(key) ? i18n.t(key) : module.description;
}
