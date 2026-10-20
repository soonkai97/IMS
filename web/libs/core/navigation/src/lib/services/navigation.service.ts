import { Injectable, InjectionToken, computed, inject } from '@angular/core';
import { ModulesStore, PermissionService } from '@ims/core/identity';
import { isDefined } from '@ims/utils';
import { MENU_GROUPS, MENU_ITEMS } from '../constants/menu.const';
import { MenuItem, NavBadgeSource, NavEntry, NavGroup } from '../types/menu.types';

export const NAV_BADGE_SOURCES = new InjectionToken<readonly NavBadgeSource[]>(
  'NAV_BADGE_SOURCES',
  {
    providedIn: 'root',
    factory: () => []
  }
);

/** The items the menu is built from. The app's list by default; a test provides its own to cover a module-tied item. */
export const MENU_ITEM_LIST = new InjectionToken<readonly MenuItem[]>('MENU_ITEM_LIST', {
  providedIn: 'root',
  factory: () => MENU_ITEMS
});

/**
 * Menus built from the user's permissions and the modules that are on: an item the role cannot use, or whose module is off, is not
 * rendered at all.
 */
@Injectable({ providedIn: 'root' })
export class NavigationService {
  private readonly permissions = inject(PermissionService);
  private readonly modules = inject(ModulesStore);
  private readonly items = inject(MENU_ITEM_LIST);
  private readonly badgeSources = inject(NAV_BADGE_SOURCES);

  private readonly $badges = computed(
    () => new Map(this.badgeSources.map(source => [source.menuId, source.$count()]))
  );

  readonly $entries = computed<readonly NavEntry[]>(() => {
    const badges = this.$badges();
    return this.items
      .filter(
        item =>
          (item.module === undefined || this.modules.isEnabled(item.module)) &&
          this.permissions.hasAny(item.permissions)
      )
      .map(item => ({
        ...item,
        badge: badges.get(item.id) ?? 0
      }));
  });

  readonly $groups = computed<readonly NavGroup[]>(() =>
    MENU_GROUPS.map(group => ({
      group,
      entries: this.$entries().filter(entry => entry.group === group.id)
    })).filter(navGroup => navGroup.entries.length > 0)
  );

  readonly $phoneTabs = computed<readonly NavEntry[]>(() => {
    const ids = this.permissions.$profile().phoneTabs;
    const entries = this.$entries();
    return ids.map(id => entries.find(entry => entry.id === id)).filter(isDefined);
  });

  readonly $moreEntries = computed<readonly NavEntry[]>(() => {
    const tabIds = new Set(this.$phoneTabs().map(entry => entry.id));
    return this.$entries().filter(entry => !tabIds.has(entry.id));
  });
}
