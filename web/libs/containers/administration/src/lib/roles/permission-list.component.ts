import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { PermissionDto } from '@ims/api';
import { I18nService, TranslatePipe, isMessageKey } from '@ims/core/i18n';
import { PermissionGroupView } from '@ims/features/administration';
import { UiIconComponent, UiStatusChipComponent } from '@ims/ui';

/**
 * What a role allows, by catalog section: a read-only list where every permission says "Allowed" or "Not allowed" in words
 * with an icon, never by colour alone (docs/design/roles-permissions-ux.md 3.12). Names and sentences are translated by code
 * (`perm.<code>`, `perm.<code>.help`); a permission added by a newer API has no text yet and shows the API's own sentence.
 */
@Component({
  selector: 'app-permission-list',
  imports: [TranslatePipe, UiIconComponent, UiStatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './permission-list.component.html',
  styleUrl: './permission-list.component.scss'
})
export class PermissionListComponent {
  private readonly i18n = inject(I18nService);

  $groups = input.required<readonly PermissionGroupView[]>({ alias: 'groups' });
  $catalog = input.required<readonly PermissionDto[]>({ alias: 'catalog' });
  $selected = input.required<ReadonlySet<string>>({ alias: 'selected' });

  private readonly $names = computed(
    () => new Map(this.$catalog().map(permission => [permission.code, this.label(permission)]))
  );

  groupLabel(group: string): string {
    const key = `perm.group.${group}`;
    return isMessageKey(key) ? this.i18n.t(key) : group;
  }

  label(permission: PermissionDto): string {
    const key = `perm.${permission.code}`;
    return isMessageKey(key) ? this.i18n.t(key) : permission.description;
  }

  help(permission: PermissionDto): string {
    const key = `perm.${permission.code}.help`;
    return isMessageKey(key) ? this.i18n.t(key) : permission.description;
  }

  isOn(permission: PermissionDto): boolean {
    return this.$selected().has(permission.code);
  }

  count(group: PermissionGroupView): number {
    return group.permissions.filter(permission => this.isOn(permission)).length;
  }

  requires(permission: PermissionDto): string {
    return permission.requires.map(code => this.$names().get(code) ?? code).join(', ');
  }
}
