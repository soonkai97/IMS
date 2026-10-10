import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18nService, ROLE_LABELS, TranslatePipe } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { UiEmptyStateComponent } from '@ims/ui';

/** E8: shown when a route needs a permission the role does not have. */
@Component({
  selector: 'app-forbidden-page',
  imports: [RouterLink, TranslatePipe, UiEmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './forbidden.component.html',
  styleUrl: './forbidden.component.scss'
})
export class ForbiddenPageComponent {
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);

  $roleLabel = computed(() => this.i18n.enumLabel(ROLE_LABELS, this.permissions.$role()));
}
