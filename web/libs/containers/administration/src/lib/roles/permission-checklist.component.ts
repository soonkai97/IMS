import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionDto } from '@ims/api';
import { I18nService, TranslatePipe, isMessageKey } from '@ims/core/i18n';
import { PermissionGroupView } from '@ims/features/administration';
import { UiCheckboxComponent, UiStatusChipComponent } from '@ims/ui';

/**
 * The permissions of a role as ticked rows, one `fieldset` with a `legend` per section. The boxes are disabled: the page is
 * view-only for now, and a disabled box still reads as "checked" or "not checked" to a screen reader.
 */
@Component({
  selector: 'app-permission-checklist',
  imports: [FormsModule, TranslatePipe, UiCheckboxComponent, UiStatusChipComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './permission-checklist.component.html',
  styleUrl: './permission-checklist.component.scss'
})
export class PermissionChecklistComponent {
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

  /** The translated name; a permission added by a newer API has no key yet and shows the API's own sentence. */
  label(permission: PermissionDto): string {
    const key = `perm.${permission.code}`;
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
