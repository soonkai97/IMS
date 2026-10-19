import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable, defaultIfEmpty, map, of, switchMap, tap } from 'rxjs';
import { RoleCreateRequest, RoleDto, RolePermissionsResult } from '@ims/api';
import { I18nService, MessageKey, TranslatePipe, isMessageKey } from '@ims/core/i18n';
import { PermissionService } from '@ims/core/identity';
import { HasUnsavedChanges } from '@ims/core/navigation';
import { ConfirmService, ToastService } from '@ims/core/notification';
import {
  RoleDetailStore,
  RoleFailure,
  RoleReadOnlyReason,
  RolesStore,
  classifyRoleFailure,
  groupPermissions,
  kindOfRole,
  notHeld,
  roleReadOnlyReason,
  sameSet,
  sortRoles,
  turnOff,
  turnOn
} from '@ims/features/administration';
import { ApiError, HTTP_STATUS, PERMISSION, PublicId } from '@ims/models';
import {
  SelectOption,
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiCheckboxComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiSelectComponent,
  UiSkeletonComponent,
  UiStatusChipComponent,
  UiTextareaComponent
} from '@ims/ui';
import { applyFieldErrors, controlSignal, normalizePublicId } from '@ims/utils';
import { PermissionChecklistComponent, PermissionToggle } from './permission-checklist.component';
import { PermissionListComponent } from './permission-list.component';

/** The API's limits for a role (Limits.RoleNameMinLength and the others). */
const NAME_MIN_LENGTH = 2;
const NAME_MAX_LENGTH = 60;
const DESCRIPTION_MAX_LENGTH = 200;

const READ_ONLY_TEXT: Record<RoleReadOnlyReason, MessageKey> = {
  locked: 'rolePage.readOnly.locked',
  viewOnly: 'rolePage.readOnly.viewOnly',
  ownRole: 'rolePage.readOnly.ownRole',
  holdsMore: 'rolePage.readOnly.holdsMore'
};

/**
 * A role page (RP02): a person who may change the role edits its name, description and state and ticks its permissions;
 * `/roles/new` creates one (blank, or copied from a role) and a created role can be deleted. Anyone else gets the read-only list
 * of RP02a. Provides its own stores, so what one signed-in person read is gone with the page. The API enforces every rule shown
 * here (what you may give, which role you may change); the screen says so first, and explains any refusal in words.
 */
@Component({
  selector: 'app-role-page',
  imports: [
    NgTemplateOutlet,
    ReactiveFormsModule,
    TranslatePipe,
    PermissionChecklistComponent,
    PermissionListComponent,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiCheckboxComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiSelectComponent,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTextareaComponent
  ],
  providers: [RolesStore, RoleDetailStore],
  host: { '(window:beforeunload)': 'onBeforeUnload($event)' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './role-page.component.html',
  styleUrl: './role-page.component.scss'
})
export class RolePageComponent implements HasUnsavedChanges {
  private readonly confirm = inject(ConfirmService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly i18n = inject(I18nService);
  private readonly permissions = inject(PermissionService);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly store = inject(RoleDetailStore);

  /** Route parameter; absent on /roles/new. */
  $id = input<string | undefined>(undefined, { alias: 'id' });

  readonly nameMaxLength = NAME_MAX_LENGTH;
  readonly descriptionMaxLength = DESCRIPTION_MAX_LENGTH;
  readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [
        Validators.required,
        Validators.minLength(NAME_MIN_LENGTH),
        Validators.maxLength(NAME_MAX_LENGTH)
      ]
    }),
    description: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(DESCRIPTION_MAX_LENGTH)]
    }),
    isActive: new FormControl(true, { nonNullable: true }),
    copyFromId: new FormControl<PublicId | null>(null)
  });

  /** The id in the URL as the API writes it; undefined for anything that is not a GUID (an old bookmark). */
  $roleId = computed(() => normalizePublicId(this.$id()));
  $isNew = computed(() => this.$id() === undefined);
  readonly $baseline = signal<RoleDto | undefined>(undefined);
  readonly $selected = signal<ReadonlySet<string>>(new Set());
  readonly $failure = signal<RoleFailure | undefined>(undefined);
  /** The page's polite live region: the note of what a tick also turned on or off. */
  readonly $announcement = signal('');
  private readonly $vanished = signal(false);
  private readonly $formDirty = signal(false);
  private readonly $copyFromId = controlSignal(this.form.controls.copyFromId);

  $catalog = this.store.catalog.$data;
  $held = this.permissions.$permissions;
  $canManage = computed(() => this.permissions.has(PERMISSION.RolesManage));
  $isLoading = computed(
    () =>
      this.store.detail.$isLoading() ||
      this.store.catalog.$isLoading() ||
      (!this.$isNew() && !this.$baseline())
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
      !this.$isNew() &&
      (this.$roleId() === undefined ||
        this.$vanished() ||
        this.store.detail.$operation().error?.status === HTTP_STATUS.NotFound)
  );
  $isSaving = computed(
    () =>
      this.store.create.$isRunning() ||
      this.store.saveDetails.$isRunning() ||
      this.store.savePermissions.$isRunning()
  );
  $isDeleting = this.store.remove.$isRunning;

  $groups = computed(() => groupPermissions(this.$catalog() ?? []));
  $readOnlyReason = computed(() => {
    const role = this.$baseline();
    return role === undefined
      ? undefined
      : roleReadOnlyReason(role, {
          catalog: this.$catalog(),
          held: this.$held(),
          canManage: this.$canManage(),
          ownRoleCode: this.permissions.$role()
        });
  });
  $editable = computed(() =>
    this.$isNew() ? this.$canManage() : this.$baseline() !== undefined && !this.$readOnlyReason()
  );
  /** A standard (system) role keeps its name, its text and its state; only its permissions change. */
  $isStandard = computed(() => this.$baseline()?.isSystem === true);
  $textEditable = computed(() => this.$editable() && !this.$isStandard());
  $canDelete = computed(
    () => !this.$isNew() && this.$editable() && this.$baseline()?.isSystem === false
  );
  /** The page always has a heading: the role's name once it is there, the list's title while it loads or fails. */
  $heading = computed(() => {
    if (this.$isNew()) {
      return this.i18n.t('rolePage.newTitle');
    }
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

  /** The roles whose permissions can be copied (a role that holds more than the person cannot be copied). */
  $copyOptions = computed<readonly SelectOption[]>(() =>
    sortRoles(this.store.roleChoices.$data() ?? []).map(role => {
      const holdsMore = notHeld(this.$held(), role.permissions).length > 0;
      return {
        value: role.id,
        label: holdsMore ? `${role.name} (${this.i18n.t('rolePage.copyHolds')})` : role.name,
        disabled: holdsMore
      };
    })
  );
  private readonly $source = computed(() =>
    (this.store.roleChoices.$data() ?? []).find(role => role.id === this.$copyFromId())
  );
  /** The source the ticks were last filled from: what a hand-made change is measured against. */
  private appliedSource: RoleDto | undefined;
  private resetting = false;

  private readonly $permissionsDirty = computed(
    () => !sameSet(this.$selected(), new Set(this.$baseline()?.permissions ?? []))
  );
  $dirty = computed(
    () => this.$permissionsDirty() || this.$formDirty() || (this.$isNew() && !!this.$copyFromId())
  );
  /** A role already saved has nothing to save until something changed; a new one always can (the form then says what is missing). */
  $canSave = computed(() => this.$isNew() || this.$dirty());

  constructor() {
    this.store.catalog.load();
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.$formDirty.set(this.form.dirty);
    });
    this.form.controls.copyFromId.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (this.$isNew() && !this.resetting) {
        this.startFrom();
      }
    });
  }

  /** Opens the role the URL names, and again when the same page is reused for another one. */
  private readonly openRole = effect(() => {
    const id = this.$roleId();
    const isNew = this.$isNew();
    untracked(() => {
      this.reset(undefined);
      this.$vanished.set(false);
      if (isNew || id === undefined) {
        this.store.detail.reset();
        if (isNew) {
          this.store.roleChoices.load({ includeInactive: false });
        }
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

  private readonly applyEnabled = effect(() => {
    const textEditable = this.$textEditable();
    untracked(() => {
      for (const control of [this.form.controls.name, this.form.controls.description]) {
        this.setEnabled(control, textEditable);
      }
      this.setEnabled(this.form.controls.isActive, textEditable && !this.$isNew());
      this.setEnabled(this.form.controls.copyFromId, this.$isNew() && this.$canManage());
    });
  });

  hasUnsavedChanges(): boolean {
    return this.$editable() && this.$dirty();
  }

  onBeforeUnload(event: BeforeUnloadEvent): void {
    if (this.hasUnsavedChanges()) {
      event.preventDefault();
    }
  }

  /**
   * Choosing what a new role starts from fills the ticks. Ticks the person made by hand are not thrown away silently: the
   * dialog asks first, and a refusal puts the previous choice back.
   */
  private startFrom(): void {
    const next = this.$source();
    const previous = this.appliedSource;
    const handMade = !sameSet(this.$selected(), new Set(previous?.permissions ?? []));
    const apply = (): void => {
      this.appliedSource = next;
      this.$selected.set(new Set(next?.permissions ?? []));
      this.$announcement.set('');
    };
    if (!handMade || next === previous) {
      apply();
      return;
    }
    this.confirm
      .ask({
        heading: this.i18n.t('rolePage.replaceTitle'),
        body: this.i18n.t('rolePage.replaceBody'),
        confirmLabel: this.i18n.t('rolePage.replace'),
        cancelLabel: this.i18n.t('common.cancel')
      })
      .pipe(
        map(() => true),
        defaultIfEmpty(false),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(confirmed => {
        if (confirmed) {
          apply();
        } else {
          this.form.controls.copyFromId.setValue(previous?.id ?? null, { emitEvent: false });
        }
      });
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

  /** The Discard button goes away with the edits, so focus moves to the page heading first (never to the body). */
  onDiscard(): void {
    this.host.nativeElement.querySelector('h1')?.focus();
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
    const reason = this.reasonText(failure);
    return failure.textSaved ? this.i18n.t('rolePage.failure.partial', { reason }) : reason;
  }

  private reasonText(failure: RoleFailure): string {
    switch (failure.kind) {
      case 'stale':
        return this.i18n.t('rolePage.failure.stale');
      case 'inUse':
        return failure.count === undefined
          ? failure.message
          : this.i18n.t('rolePage.failure.inUse', { count: failure.count });
      default:
        return failure.message;
    }
  }

  onSave(): void {
    this.form.markAllAsTouched();
    const base = this.$baseline();
    if (this.form.invalid) {
      this.host.nativeElement
        .querySelector<HTMLElement>(
          '[formControlName].ng-invalid input, [formControlName].ng-invalid textarea'
        )
        ?.focus();
      return;
    }
    if (!this.$editable() || this.$isSaving() || (!this.$isNew() && !base)) {
      return;
    }
    this.$failure.set(undefined);
    this.store.create.reset();
    this.store.saveDetails.reset();
    this.store.savePermissions.reset();
    if (this.$isNew()) {
      this.create();
    } else if (base) {
      this.update(base);
    }
  }

  onDelete(): void {
    const role = this.$baseline();
    if (!role || !this.$canDelete()) {
      return;
    }
    this.$failure.set(undefined);
    this.store.remove.reset();
    this.confirm
      .ask({
        heading: this.i18n.t('rolePage.deleteTitle', { name: role.name }),
        body: this.i18n.t('rolePage.deleteBody'),
        confirmLabel: this.i18n.t('rolePage.delete'),
        cancelLabel: this.i18n.t('common.cancel'),
        destructive: true
      })
      .pipe(
        switchMap(() => this.store.remove.run(role.id)),
        tap(() => {
          this.reset(role);
          this.toast.success(this.i18n.t('rolePage.deleted', { name: role.name }));
          void this.router.navigate(['/roles']);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        complete: () => {
          const error = this.store.remove.$operation().error;
          if (error) {
            this.explain(error, { removing: true });
            // The dialog is gone: focus goes back to the button that opened it, never to the body.
            afterNextRender(
              () =>
                this.host.nativeElement.querySelector<HTMLElement>('[data-delete] button')?.focus(),
              { injector: this.injector }
            );
          }
        }
      });
  }

  private create(): void {
    const value = this.form.getRawValue();
    const source = this.$source();
    // A role copied and left as it is keeps its link in the audit trail (the API copies at that moment); an edited set is sent as is.
    const copyOf =
      source !== undefined && sameSet(this.$selected(), new Set(source.permissions))
        ? source.id
        : undefined;
    const body: RoleCreateRequest = {
      name: value.name.trim(),
      description: value.description.trim(),
      ...(copyOf === undefined
        ? { permissions: [...this.$selected()] }
        : { copyFromRoleId: copyOf })
    };
    this.store.create
      .run(body)
      .pipe(
        tap(role => {
          this.reset(role);
          this.toast.success(this.i18n.t('rolePage.created', { name: role.name }));
          void this.router.navigate(['/roles', role.id], { replaceUrl: true, scroll: 'manual' });
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        complete: () =>
          this.afterWrite(this.store.create.$operation().error, { onName: true, textSaved: false })
      });
  }

  /** Saves the text first (a standard role has none to save), then the permissions with the token of the previous answer. */
  private update(base: RoleDto): void {
    const value = this.form.getRawValue();
    const body = {
      name: value.name.trim(),
      description: value.description.trim(),
      isActive: value.isActive
    };
    const detailsChanged =
      this.$textEditable() &&
      (body.name !== base.name ||
        body.description !== base.description ||
        body.isActive !== base.isActive);
    const permissionsChanged = this.$permissionsDirty();
    if (!detailsChanged && !permissionsChanged) {
      return;
    }
    let result: RolePermissionsResult | undefined;
    let textSaved = false;
    const details$: Observable<unknown> = detailsChanged
      ? this.store.saveDetails.run({ id: base.id, token: base.concurrencyToken, body }).pipe(
          tap(role => {
            textSaved = true;
            this.$baseline.set(role);
          })
        )
      : of(undefined);
    details$
      .pipe(
        switchMap(() => {
          const current = this.$baseline() ?? base;
          return permissionsChanged
            ? this.store.savePermissions
                .run({
                  id: current.id,
                  token: current.concurrencyToken,
                  permissions: [...this.$selected()]
                })
                .pipe(
                  tap(saved => {
                    result = saved;
                    this.$baseline.set(saved.role);
                  })
                )
            : of(undefined);
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        complete: () => {
          const detailsError = this.store.saveDetails.$operation().error;
          const error = detailsError ?? this.store.savePermissions.$operation().error;
          if (error) {
            if (textSaved) {
              // The text is the new baseline; only the ticks are still unsaved.
              this.form.markAsPristine();
              this.$formDirty.set(false);
            }
            this.afterWrite(error, { onName: detailsError !== undefined, textSaved });
            return;
          }
          this.reset(this.$baseline());
          // The toast is the one announcement of the result (it is a live region itself).
          this.toast.success(
            result === undefined
              ? this.i18n.t('rolePage.savedDetails')
              : this.i18n.t('rolePage.saved', {
                  added: result.added.length,
                  removed: result.removed.length
                })
          );
        }
      });
  }

  private afterWrite(
    error: ApiError | undefined,
    options: { onName: boolean; textSaved: boolean }
  ): void {
    if (error) {
      this.explain(error, { removing: false, ...options });
    }
  }

  /**
   * Shows why a write was refused. A 409 is told apart by reading the role again: another concurrency token than the one the
   * save started from means someone changed it (the API gives no code). A name that is taken lands on the Name field.
   */
  private explain(
    error: ApiError,
    options: { removing: boolean; onName?: boolean; textSaved?: boolean }
  ): void {
    const base = this.$baseline();
    const changed$ =
      error.status === HTTP_STATUS.Conflict && base !== undefined && !options.removing
        ? this.store.changedSince(base.id, base.concurrencyToken)
        : of(false);
    changed$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(roleChangedSince => {
      const failure = classifyRoleFailure(error, { roleChangedSince });
      if (failure.kind === 'notFound' && !this.$isNew()) {
        this.$vanished.set(true);
      } else if (failure.kind === 'conflict' && options.onName) {
        this.form.controls.name.setErrors({ server: failure.message });
        this.form.controls.name.markAsTouched();
      } else if (failure.kind === 'invalid' && Object.keys(error.fieldErrors).length > 0) {
        const unmatched = applyFieldErrors(this.form, error);
        if (unmatched.length > 0) {
          this.$failure.set({ ...failure, message: unmatched[0] });
        }
      } else {
        this.$failure.set({ ...failure, textSaved: options.textSaved });
      }
    });
  }

  /** Puts a role (or the empty form of a new role) on screen and clears every unsaved edit. */
  private reset(role: RoleDto | undefined): void {
    this.$baseline.set(role);
    this.resetting = true;
    this.form.reset({
      name: role?.name ?? '',
      description: role?.description ?? '',
      isActive: role?.isActive ?? true,
      copyFromId: null
    });
    this.resetting = false;
    this.appliedSource = undefined;
    this.$formDirty.set(false);
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

  private setEnabled(control: FormControl, enabled: boolean): void {
    if (enabled && control.disabled) {
      control.enable({ emitEvent: false });
    } else if (!enabled && control.enabled) {
      control.disable({ emitEvent: false });
    }
  }
}
