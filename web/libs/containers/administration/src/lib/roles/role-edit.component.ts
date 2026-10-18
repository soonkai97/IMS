import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  untracked
} from '@angular/core';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import {
  RoleEditorStore,
  RolesStore,
  groupPermissions,
  isLockedRole,
  kindOfRole
} from '@ims/features/administration';
import { HTTP_STATUS } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiEmptyStateComponent,
  UiIconComponent,
  UiPageHeaderComponent,
  UiSkeletonComponent,
  UiStatusChipComponent
} from '@ims/ui';
import { normalizePublicId } from '@ims/utils';
import { PermissionChecklistComponent } from './permission-checklist.component';

/**
 * A role and what it allows (RP02, view only): its text, its state and the permissions by section, each with the API's
 * sentence. Provides its own stores, so what one signed-in person read is gone with the page. Which role is locked and which
 * is a standard one comes from the data, never from a role name.
 */
@Component({
  selector: 'app-role-edit-page',
  imports: [
    TranslatePipe,
    PermissionChecklistComponent,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiStatusChipComponent
  ],
  providers: [RolesStore, RoleEditorStore],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './role-edit.component.html',
  styleUrl: './role-edit.component.scss'
})
export class RoleEditPageComponent {
  private readonly i18n = inject(I18nService);
  private readonly store = inject(RoleEditorStore);

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  /** The id in the URL as the API writes it; undefined for anything that is not a GUID (an old bookmark). */
  $roleId = computed(() => normalizePublicId(this.$id()));
  $catalog = this.store.catalog.$data;
  /** The role named by the URL; the previous page's role is never shown for another id. */
  $role = computed(() => {
    const role = this.store.detail.$data();
    return role !== undefined && role.id === this.$roleId() ? role : undefined;
  });
  $notFound = computed(
    () =>
      this.$roleId() === undefined ||
      this.store.detail.$operation().error?.status === HTTP_STATUS.NotFound
  );
  $loadFailed = computed(
    () =>
      (this.store.detail.$isFailed() &&
        this.store.detail.$operation().error?.status !== HTTP_STATUS.NotFound) ||
      this.store.catalog.$isFailed()
  );
  $groups = computed(() => groupPermissions(this.$catalog() ?? []));
  $selected = computed<ReadonlySet<string>>(() => new Set(this.$role()?.permissions));
  $isLocked = computed(() => {
    const role = this.$role();
    return role !== undefined && isLockedRole(role, this.$catalog());
  });
  $heading = computed(() =>
    this.$notFound() ? this.i18n.t('notFound.title') : (this.$role()?.name ?? '')
  );
  $kindLabel = computed(() => {
    const role = this.$role();
    return role === undefined ? '' : this.i18n.t(`roles.kind.${kindOfRole(role, this.$catalog())}`);
  });

  /** Opens the role the URL names, and again when the same page is reused for another one (Back/Forward). */
  private readonly openRole = effect(() => {
    const id = this.$roleId();
    untracked(() => {
      if (id === undefined) {
        this.store.detail.reset();
        return;
      }
      this.store.detail.load(id);
    });
  });

  constructor() {
    this.store.catalog.load();
  }

  onRetry(): void {
    this.store.catalog.reload();
    this.store.detail.reload();
  }
}
