import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { of, tap } from 'rxjs';
import { RoleDto } from '@ims/api';
import { I18nService, MessageKey, TranslatePipe, isMessageKey } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { HasUnsavedChanges } from '@ims/core/navigation';
import { ToastService } from '@ims/core/notification';
import {
  RoleDetailStore,
  RoleFailure,
  RoleReadOnlyReason,
  RolesStore,
  classifyRoleFailure,
  groupPermissions,
  kindOfRole,
  roleReadOnlyReason,
  sameSet,
  turnOff,
  turnOn
} from '@ims/features/administration';
import { ApiError, HTTP_STATUS, PERMISSION } from '@ims/models';
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
import { PermissionChecklistComponent, PermissionToggle } from './permission-checklist.component';
import { PermissionListComponent } from './permission-list.component';

const READ_ONLY_TEXT: Record<RoleReadOnlyReason, MessageKey> = {
  locked: 'rolePage.readOnly.locked',
  viewOnly: 'rolePage.readOnly.viewOnly',
  ownRole: 'rolePage.readOnly.ownRole',
  holdsMore: 'rolePage.readOnly.holdsMore'
};

/**
 * A role page (RP02): a person who may change the role ticks its permissions and saves; anyone else gets the read-only list of
 * RP02a. Provides its own stores, so what one signed-in person read is gone with the page. The API enforces every rule shown
 * here (what you may give, which role you may change); the screen says so first, and explains any refusal in words.
 */
@Component({
  selector: 'app-role-page',
  imports: [
    TranslatePipe,
    PermissionChecklistComponent,
    PermissionListComponent,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiEmptyStateComponent,
    UiIconComponent,
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiStatusChipComponent
  ],
  providers: [RolesStore, RoleDetailStore],
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './role-page.component.html',
  styleUrl: './role-page.component.scss'
})
export class RolePageComponent implements HasUnsavedChanges {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly toast = inject(ToastService);
  private readonly store = inject(RoleDetailStore);

  /** Route parameter. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  /** The id in the URL as the API writes it; undefined for anything that is not a GUID (an old bookmark). */
  $roleId = computed(() => normalizePublicId(this.$id()));
  /** The role as last read or saved: what the unsaved ticks are compared with. */
  readonly $baseline = signal<RoleDto | undefined>(undefined);
  /** The ticked permissions, including the unsaved ones. */
  readonly $selected = signal<ReadonlySet<string>>(new Set());
  readonly $failure = signal<RoleFailure | undefined>(undefined);
  /** The one polite live region: dependency notices and the result of a save. */
  readonly $announcement = signal('');
  private readonly $vanished = signal(false);

  $catalog = this.store.catalog.$data;
  $held = this.permissions.$permissions;
  $isLoading = computed(
    () => this.store.detail.$isLoading() || this.store.catalog.$isLoading() || !this.$baseline()
  );
  $loadFailed = computed(() => {
    const status = this.store.detail.$operation().error?.status;
    return (
      (this.store.detail.$isFailed() && status !== HTTP_STATUS.NotFound) ||
      this.store.catalog.$isFailed()
    );
  });
  $notFound = computed(
    () =>
      this.$roleId() === undefined ||
      this.$vanished() ||
      this.store.detail.$operation().error?.status === HTTP_STATUS.NotFound
  );
  $isSaving = this.store.savePermissions.$isRunning;

  $groups = computed(() => groupPermissions(this.$catalog() ?? []));
  $readOnlyReason = computed(() => {
    const role = this.$baseline();
    return role === undefined
      ? undefined
      : roleReadOnlyReason(role, {
          catalog: this.$catalog(),
          held: this.$held(),
          canManage: this.permissions.has(PERMISSION.RolesManage),
          ownRoleCode: this.permissions.$role()
        });
  });
  $editable = computed(() => this.$baseline() !== undefined && !this.$readOnlyReason());
  /** The page always has a heading: the role's name once it is there, the list's title while it loads or fails. */
  $heading = computed(() => {
    if (this.$notFound()) {
      return this.i18n.t('notFound.title');
    }
    return this.$baseline()?.name ?? this.i18n.t('roles.title');
  });
  $kindLabel = computed(() => {
    const role = this.$baseline();
    return role === undefined ? '' : this.i18n.t(`roles.kind.${kindOfRole(role, this.$catalog())}`);
  });
  $readOnlyText = computed(() => {
    const reason = this.$readOnlyReason();
    return reason === undefined ? '' : this.i18n.t(READ_ONLY_TEXT[reason]);
  });
  $dirty = computed(() => !sameSet(this.$selected(), new Set(this.$baseline()?.permissions ?? [])));

  constructor() {
    this.store.catalog.load();
  }

  /** Opens the role the URL names, and again when the same page is reused for another one. */
  private readonly openRole = effect(() => {
    const id = this.$roleId();
    untracked(() => {
      this.reset(undefined);
      this.$vanished.set(false);
      if (id === undefined) {
        this.store.detail.reset();
        return;
      }
      this.store.detail.load(id);
    });
  });

  private readonly populate = effect(() => {
    const role = this.store.detail.$data();
    if (role && role.id === this.$roleId()) {
      untracked(() => this.reset(role));
    }
  });

  hasUnsavedChanges(): boolean {
    return this.$editable() && this.$dirty();
  }

  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  onToggle(toggle: PermissionToggle): void {
    const catalog = this.$catalog();
    if (!this.$editable() || catalog === undefined) {
      return;
    }
    const result = toggle.on
      ? turnOn(this.$selected(), catalog, toggle.code)
      : turnOff(this.$selected(), catalog, toggle.code);
    this.$selected.set(result.selected);
    this.$announcement.set(
      result.also.length === 0
        ? ''
        : this.i18n.t(toggle.on ? 'rolePage.alsoOn' : 'rolePage.alsoOff', {
            names: this.namesOf(result.also)
          })
    );
  }

  onDiscard(): void {
    this.reset(this.$baseline());
  }

  onReload(): void {
    this.$failure.set(undefined);
    this.store.detail.reload();
  }

  onRetryLoad(): void {
    this.store.catalog.reload();
    this.store.detail.reload();
  }

  failureText(failure: RoleFailure): string {
    switch (failure.kind) {
      case 'stale':
        return this.i18n.t('rolePage.failure.stale');
      case 'containment':
        return this.i18n.t('rolePage.failure.containment');
      default:
        return failure.message;
    }
  }

  /** Sends the whole set with the token the page read (If-Match); the answer says what was added and removed. */
  onSave(): void {
    const base = this.$baseline();
    if (!base || !this.$editable() || !this.$dirty() || this.$isSaving()) {
      return;
    }
    this.$failure.set(undefined);
    this.store.savePermissions.reset();
    this.store.savePermissions
      .run({ id: base.id, token: base.concurrencyToken, permissions: [...this.$selected()] })
      .pipe(
        tap(saved => {
          const message = this.i18n.t('rolePage.saved', {
            added: saved.added.length,
            removed: saved.removed.length
          });
          this.reset(saved.role);
          this.toast.success(message);
          this.$announcement.set(message);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        complete: () => {
          const error = this.store.savePermissions.$operation().error;
          if (error) {
            this.explain(error);
          }
        }
      });
  }

  /**
   * Shows why a write was refused. A 409 is told apart by reading the role again: another concurrency token than the one the
   * save started from means someone changed it (the API gives no code).
   */
  private explain(error: ApiError): void {
    const base = this.$baseline();
    const changed$ =
      error.status === HTTP_STATUS.Conflict && base !== undefined
        ? this.store.changedSince(base.id, base.concurrencyToken)
        : of(false);
    changed$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(roleChangedSince => {
      const failure = classifyRoleFailure(error, { roleChangedSince });
      if (failure.kind === 'notFound') {
        this.$vanished.set(true);
      } else {
        this.$failure.set(failure);
      }
    });
  }

  /** Puts a role on screen and clears every unsaved tick. */
  private reset(role: RoleDto | undefined): void {
    this.$baseline.set(role);
    this.$selected.set(new Set(role?.permissions ?? []));
    this.$failure.set(undefined);
    this.$announcement.set('');
  }

  private namesOf(codes: readonly string[]): string {
    const catalog = this.$catalog() ?? [];
    return codes
      .map(code => {
        const key = `perm.${code}`;
        return isMessageKey(key)
          ? this.i18n.t(key)
          : (catalog.find(permission => permission.code === code)?.description ?? code);
      })
      .join(', ');
  }
}
