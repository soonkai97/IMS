import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
  computed,
  effect,
  inject,
  signal,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { finalize, switchMap, tap } from 'rxjs';
import { ModuleDto, ModuleSwitchResult } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { ModulesStore, PermissionService, moduleDescription, moduleName } from '@ims/core/identity';
import { ConfirmService, ToastService } from '@ims/core/notification';
import { ApiError, HTTP_STATUS, MIN_REASON_LENGTH, PERMISSION } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiEmptyStateComponent,
  UiPageHeaderComponent,
  UiSkeletonComponent,
  UiSwitchComponent
} from '@ims/ui';

/** One row of the list: the module in the user's language plus what the switch needs. */
interface ModuleRow {
  readonly module: ModuleDto;
  readonly name: string;
  readonly description: string;
  /** Names of the modules that must be on first (empty: none). */
  readonly requires: string;
  readonly nameId: string;
}

/**
 * Settings > Modules (needs `modules.manage`): which parts of the system are on for the whole installation. Flipping a switch asks
 * for a reason (the same 5-character rule as the API), then shows the answer in one toast, reloads the list and so the menu follows.
 * Every failure of the call is shown here in a banner, not as a toast.
 */
@Component({
  selector: 'app-modules-page',
  imports: [
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiEmptyStateComponent,
    UiPageHeaderComponent,
    UiSkeletonComponent,
    UiSwitchComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './modules.component.html',
  styleUrl: './modules.component.scss'
})
export class ModulesPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly i18n = inject(I18nService);
  private readonly store = inject(ModulesStore);
  private readonly confirm = inject(ConfirmService);
  private readonly toast = inject(ToastService);
  private readonly permissions = inject(PermissionService);

  private destroyed = false;

  /** The sentence of the last failed switch (the banner); cleared when the next one starts. */
  readonly $problem = signal<string | undefined>(undefined);

  $isLoading = this.store.list.$isLoading;
  $loadFailed = this.store.list.$isFailed;
  $isSwitching = this.store.switchModule.$isRunning;
  /** The Settings link above the title only for someone who may open Settings (it is a different permission). */
  $backLink = computed(() =>
    this.permissions.has(PERMISSION.SettingsManage) ? ['/settings'] : undefined
  );
  $rows = computed<readonly ModuleRow[]>(() => {
    const modules = this.store.list.$data() ?? [];
    const names = new Map(modules.map(module => [module.code, moduleName(this.i18n, module)]));
    return modules.map(module => ({
      module,
      name: names.get(module.code) ?? module.name,
      description: moduleDescription(this.i18n, module),
      requires: module.requires.map(code => names.get(code) ?? code).join(', '),
      nameId: `modules-page-name-${module.code}`
    }));
  });
  $showSkeleton = computed(() => this.$isLoading() && this.store.list.$data() === undefined);

  private readonly showFailure = effect(() => {
    const error = this.store.switchModule.$operation().error;
    if (error) {
      untracked(() => this.onSwitchFailed(error));
    }
  });

  constructor() {
    // The store lives for the whole session: a failure left by an earlier visit must not show again, nor reload the list.
    this.store.switchModule.reset();
    this.destroyRef.onDestroy(() => (this.destroyed = true));
  }

  ngOnInit(): void {
    this.onLoad();
  }

  onLoad(): void {
    this.store.list.load(undefined, handledInline());
  }

  /** The switch asked for `wanted`; nothing changes until the person gave a reason and the server agreed. */
  onSwitch(row: ModuleRow, wanted: boolean): void {
    this.$problem.set(undefined);
    this.store.switchModule.reset();
    this.confirm
      .ask({
        heading: this.i18n.t(wanted ? 'modules.confirm.onTitle' : 'modules.confirm.offTitle', {
          name: row.name
        }),
        body: this.i18n.t(wanted ? 'modules.confirm.onBody' : 'modules.confirm.offBody'),
        confirmLabel: this.i18n.t(wanted ? 'modules.confirm.on' : 'modules.confirm.off'),
        cancelLabel: this.i18n.t('common.cancel'),
        reasonLabel: this.i18n.t('dialog.reason'),
        reasonRequired: true,
        minReasonLength: MIN_REASON_LENGTH
      })
      .pipe(
        switchMap(reason =>
          this.store.switchModule.run({ code: row.module.code, enabled: wanted, reason })
        ),
        tap(result => this.announce(row, result)),
        finalize(() => this.returnFocus(row)),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  /**
   * The reason dialog is gone from the page when the answer comes (or the person cancelled), so the browser has no element to give the
   * focus back to: put it on the switch that was used, once the dialog has left the page.
   */
  private returnFocus(row: ModuleRow): void {
    if (this.destroyed) {
      return;
    }
    afterNextRender(
      () =>
        this.host.nativeElement
          .querySelector<HTMLElement>(`button[role='switch'][aria-labelledby='${row.nameId}']`)
          ?.focus(),
      { injector: this.injector }
    );
  }

  /** One sentence in one toast: the new state, and for a turn-on how many roles have their saved permissions back. */
  private announce(row: ModuleRow, result: ModuleSwitchResult): void {
    const state = this.i18n.t(result.enabled ? 'modules.toast.on' : 'modules.toast.off', {
      name: row.name
    });
    const regain =
      result.rolesRegainingGrants > 0
        ? ` ${this.i18n.t('modules.toast.regain', { count: result.rolesRegainingGrants })}`
        : '';
    this.toast.success(`${state}${regain}`);
  }

  private onSwitchFailed(error: ApiError): void {
    switch (error.status) {
      case HTTP_STATUS.Forbidden:
        this.$problem.set(this.i18n.t('modules.error.forbidden'));
        break;
      case HTTP_STATUS.NotFound:
        this.$problem.set(this.i18n.t('modules.error.notFound'));
        this.onLoad();
        break;
      case HTTP_STATUS.Conflict:
        this.$problem.set(this.i18n.t('modules.error.busy'));
        this.onLoad();
        break;
      default:
        // 400 (the API names the rule), 5xx and no network: the interceptor already put the right sentence in `message`.
        this.$problem.set(error.message);
    }
  }
}
