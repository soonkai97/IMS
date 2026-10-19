import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PermissionDto } from '@ims/api';
import { TranslatePipe } from '@ims/core/i18n';
import { UiCheckboxComponent, UiIconComponent, UiStatusChipComponent } from '@ims/ui';
import { PermissionListComponent } from './permission-list.component';

export interface PermissionToggle {
  readonly code: string;
  readonly on: boolean;
}

/**
 * The permissions of a role as ticked rows, one `fieldset` with a `legend` per section. It reads like the read-only list
 * (same names, sentences, counts and styles); a row is disabled, with its reason written beside it, when the person cannot give
 * a permission they do not hold. The box is named by the permission alone; its sentences are its description.
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
  styleUrl: './permission-list.component.scss'
})
export class PermissionChecklistComponent extends PermissionListComponent {
  /** What the signed-in person holds: they can give only these. */
  $held = input.required<ReadonlySet<string>>({ alias: 'held' });
  $toggled = output<PermissionToggle>({ alias: 'toggled' });

  descriptionId(permission: PermissionDto): string {
    return `permission-details-${permission.code}`;
  }

  notHeld(permission: PermissionDto): boolean {
    return !this.$held().has(permission.code) && !this.isOn(permission);
  }

  onToggle(permission: PermissionDto, on: boolean): void {
    if (!this.notHeld(permission) && on !== this.isOn(permission)) {
      this.$toggled.emit({ code: permission.code, on });
    }
  }
}
