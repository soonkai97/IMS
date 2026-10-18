import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionDto } from '@ims/api';
import { I18nService, TranslatePipe, isMessageKey } from '@ims/core/i18n';
import { PermissionGroupView } from '@ims/features/administration';
import { UiCheckboxComponent, UiIconComponent, UiStatusChipComponent } from '@ims/ui';

export interface PermissionToggle {
  readonly code: string;
  readonly on: boolean;
}

/**
 * The permissions of a role as ticked rows, one `fieldset` with a `legend` per section. A row is disabled, with its reason
 * written beside it, when the whole list is read-only or when the person cannot give a permission they do not hold. A held
 * permission that is ticked can always be unticked.
 */
@Component({
  selector: 'app-permission-checklist',
  imports: [
    FormsModule,
    TranslatePipe,
    UiCheckboxComponent,
    UiIconComponent,
    UiStatusChipComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './permission-checklist.component.html',
  styleUrl: './permission-checklist.component.scss'
})
export class PermissionChecklistComponent {
  private readonly i18n = inject(I18nService);

  $groups = input.required<readonly PermissionGroupView[]>({ alias: 'groups' });
  $catalog = input.required<readonly PermissionDto[]>({ alias: 'catalog' });
  $selected = input.required<ReadonlySet<string>>({ alias: 'selected' });
  /** What the signed-in person holds: they can give only these. */
  $held = input.required<ReadonlySet<string>>({ alias: 'held' });
  /** The whole list is view-only; the page says why. */
  $readOnly = input(false, { alias: 'readOnly' });
  $toggled = output<PermissionToggle>({ alias: 'toggled' });

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

  notHeld(permission: PermissionDto): boolean {
    return !this.$held().has(permission.code) && !this.isOn(permission);
  }

  isDisabled(permission: PermissionDto): boolean {
    return this.$readOnly() || this.notHeld(permission);
  }

  count(group: PermissionGroupView): number {
    return group.permissions.filter(permission => this.isOn(permission)).length;
  }

  requires(permission: PermissionDto): string {
    return permission.requires.map(code => this.$names().get(code) ?? code).join(', ');
  }

  onToggle(permission: PermissionDto, on: boolean): void {
    if (!this.isDisabled(permission) && on !== this.isOn(permission)) {
      this.$toggled.emit({ code: permission.code, on });
    }
  }
}
