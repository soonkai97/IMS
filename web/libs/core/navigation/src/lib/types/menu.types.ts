import { Signal } from '@angular/core';
import type { MessageKey } from '@ims/core/i18n';
import type { IconName } from '@ims/ui';

export type MenuGroupId = 'main' | 'documents' | 'stock' | 'master' | 'insight' | 'admin';

export interface MenuGroup {
  readonly id: MenuGroupId;
  readonly labelKey: MessageKey;
}

export interface MenuItem {
  readonly id: string;
  readonly labelKey: MessageKey;
  readonly icon: IconName;
  readonly route: string;
  /** Item shows when the user holds any of these (empty = everybody). */
  readonly permissions: readonly string[];
  readonly group: MenuGroupId;
  /**
   * Code of the module this item belongs to: it shows only while the module is on AND a listed permission is held (ADR
   * modules-and-tracking-dimensions 4.5). Leave it out for the core, which is always on.
   */
  readonly module?: string;
  /** The link is active only on exactly this address, not on the pages below it (Settings, which has Modules below it). */
  readonly exactMatch?: boolean;
}

/** A menu item plus its live badge (waiting approvals, low-stock count). */
export interface NavEntry extends MenuItem {
  readonly badge: number;
}

export interface NavGroup {
  readonly group: MenuGroup;
  readonly entries: readonly NavEntry[];
}

/** Lets a higher tier feed a menu badge without core depending on features. Provided in the app's config. */
export interface NavBadgeSource {
  readonly menuId: string;
  readonly $count: Signal<number>;
}
