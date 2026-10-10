import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { tap } from 'rxjs';
import { ApprovalRuleDto, DocumentType } from '@ims/api';
import {
  AppMoneyPipe,
  AppQtyPipe,
  DOCUMENT_TYPE_LABELS,
  I18nService,
  ROLE_LABELS,
  TranslatePipe
} from '@ims/core/i18n';
import { ToastService } from '@ims/core/notification';
import { SystemRulesStore } from '@ims/features/administration';
import { ROLE } from '@ims/models';
import {
  SelectOption,
  TableColumn,
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiCellDirective,
  UiCheckboxComponent,
  UiDialogComponent,
  UiEmptyStateComponent,
  UiFormFieldComponent,
  UiIconComponent,
  UiInputComponent,
  UiPageHeaderComponent,
  UiSelectComponent,
  UiSkeletonComponent,
  UiStatusChipComponent,
  UiTableComponent
} from '@ims/ui';
import { applyFieldErrors } from '@ims/utils';

const MAX_COOLDOWN_HOURS = 24 * 30;

interface RuleFormValue {
  readonly docType: DocumentType;
  readonly amountThreshold: number | null;
  readonly qtyThreshold: number | null;
  readonly roleRequired: string;
  readonly isActive: boolean;
}

/** System switches and approval rules (admin). */
@Component({
  selector: 'app-settings-page',
  imports: [
    AppMoneyPipe,
    AppQtyPipe,
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiCellDirective,
    UiCheckboxComponent,
    UiDialogComponent,
    UiEmptyStateComponent,
    UiFormFieldComponent,
    UiIconComponent,
    UiInputComponent,
    UiPageHeaderComponent,
    UiSelectComponent,
    UiSkeletonComponent,
    UiStatusChipComponent,
    UiTableComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsPageComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly store = inject(SystemRulesStore);
  private readonly toast = inject(ToastService);

  readonly settingsForm = new FormGroup({
    allowNegativeStock: new FormControl(false, { nonNullable: true }),
    allowSelfApproval: new FormControl(false, { nonNullable: true }),
    lowStockAlertCooldownHours: new FormControl<number | null>(24, [
      Validators.required,
      Validators.min(0),
      Validators.max(MAX_COOLDOWN_HOURS)
    ])
  });
  readonly ruleForm = new FormGroup({
    docType: new FormControl<DocumentType>(DocumentType.Issue, { nonNullable: true }),
    amountThreshold: new FormControl<number | null>(null, [Validators.min(0)]),
    qtyThreshold: new FormControl<number | null>(null, [Validators.min(0)]),
    roleRequired: new FormControl<string>(ROLE.Supervisor, { nonNullable: true }),
    isActive: new FormControl(true, { nonNullable: true })
  });

  $ruleDialogOpen = signal(false);
  $editingRuleId = signal<number | undefined>(undefined);

  $settings = this.store.settings.$data;
  $rules = computed(() => this.store.rules.$data() ?? []);
  $isLoading = this.store.settings.$isLoading;
  $isSavingSettings = this.store.saveSettings.$isRunning;
  $isSavingRule = this.store.saveRule.$isRunning;
  $docTypeOptions = computed<readonly SelectOption[]>(() => [
    { value: DocumentType.Issue, label: this.i18n.t('docType.Issue') },
    { value: DocumentType.Receipt, label: this.i18n.t('docType.Receipt') }
  ]);
  $roleOptions = computed<readonly SelectOption[]>(() =>
    [ROLE.Supervisor, ROLE.Owner, ROLE.Admin].map(role => ({
      value: role,
      label: this.i18n.enumLabel(ROLE_LABELS, role)
    }))
  );
  $ruleColumns = computed<readonly TableColumn<ApprovalRuleDto>[]>(() => [
    {
      key: 'docType',
      header: this.i18n.t('doc.type'),
      value: row => this.i18n.enumLabel(DOCUMENT_TYPE_LABELS, row.docType),
      primary: true
    },
    { key: 'amount', header: this.i18n.t('rule.amountThreshold'), align: 'end' },
    { key: 'qty', header: this.i18n.t('rule.qtyThreshold'), align: 'end' },
    {
      key: 'role',
      header: this.i18n.t('rule.roleRequired'),
      value: row => this.i18n.enumLabel(ROLE_LABELS, row.roleRequired)
    },
    { key: 'status', header: this.i18n.t('product.status') },
    { key: 'actions', header: this.i18n.t('common.actions'), align: 'end' }
  ]);
  $ruleHeading = computed(() =>
    this.i18n.t(this.$editingRuleId() === undefined ? 'rule.new' : 'rule.edit')
  );
  $ruleServerErrors = computed(() => {
    const error = this.store.saveRule.$operation().error;
    if (!error) {
      return [];
    }
    return Object.entries(error.fieldErrors)
      .filter(([field]) => !this.ruleForm.get(field))
      .flatMap(([, messages]) => messages);
  });

  private readonly populateSettings = effect(() => {
    const settings = this.store.settings.$data();
    if (settings) {
      this.settingsForm.reset(settings);
    }
  });

  private readonly showRuleErrors = effect(() => {
    const error = this.store.saveRule.$operation().error;
    if (error) {
      applyFieldErrors(this.ruleForm, error);
    }
  });

  ngOnInit(): void {
    this.store.settings.load();
    this.store.rules.load();
  }

  onSaveSettings(): void {
    this.settingsForm.markAllAsTouched();
    if (this.settingsForm.invalid) {
      return;
    }
    const value = this.settingsForm.getRawValue();
    this.store.saveSettings
      .run({
        allowNegativeStock: value.allowNegativeStock,
        allowSelfApproval: value.allowSelfApproval,
        lowStockAlertCooldownHours: value.lowStockAlertCooldownHours ?? 0
      })
      .pipe(
        tap(() => this.toast.success(this.i18n.t('common.saved'))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  onAddRule(): void {
    this.openRule(undefined, {
      docType: DocumentType.Issue,
      amountThreshold: null,
      qtyThreshold: null,
      roleRequired: ROLE.Supervisor,
      isActive: true
    });
  }

  onEditRule(rule: ApprovalRuleDto): void {
    this.openRule(rule.id, {
      docType: rule.docType,
      amountThreshold: rule.amountThreshold ?? null,
      qtyThreshold: rule.qtyThreshold ?? null,
      roleRequired: rule.roleRequired,
      isActive: rule.isActive
    });
  }

  onSaveRule(): void {
    this.ruleForm.markAllAsTouched();
    if (this.ruleForm.invalid) {
      return;
    }
    const value = this.ruleForm.getRawValue();
    this.store.saveRule
      .run({
        id: this.$editingRuleId(),
        body: {
          docType: value.docType,
          amountThreshold: value.amountThreshold ?? undefined,
          qtyThreshold: value.qtyThreshold ?? undefined,
          roleRequired: value.roleRequired,
          isActive: value.isActive
        }
      })
      .pipe(
        tap(() => {
          this.$ruleDialogOpen.set(false);
          this.toast.success(this.i18n.t('common.saved'));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private openRule(id: number | undefined, value: RuleFormValue): void {
    this.store.saveRule.reset();
    this.ruleForm.reset(value);
    this.$editingRuleId.set(id);
    this.$ruleDialogOpen.set(true);
  }
}
