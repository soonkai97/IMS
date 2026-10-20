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
  untracked,
  viewChild
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { I18nService, TranslatePipe } from '@ims/core/i18n';
import { AuthService, HOME_ROUTE } from '@ims/core/identity';
import { API_ERROR_KIND, ApiError, MAX_PASSWORD_LENGTH, MIN_PASSWORD_LENGTH } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiCardComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiInputComponent,
  UiPageHeaderComponent
} from '@ims/ui';
import { SERVER_ERROR_KEY, applyFieldErrors, focusFirstInvalid } from '@ims/utils';
import { createChangePasswordForm } from './change-password-form';

/**
 * The signed-in user changes their own password (`/account/password`, entered from the Account dialog). The checks run first;
 * on success the server has ended every session, so the app clears this one and opens the sign-in page with a notice.
 * Nothing typed here is logged, put in a URL or a toast, or stored.
 */
@Component({
  selector: 'app-change-password-page',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiCardComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiInputComponent,
    UiPageHeaderComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './change-password.component.html',
  styleUrl: './change-password.component.scss',
  host: { '(document:visibilitychange)': 'onVisibility()' }
})
export class ChangePasswordPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly router = inject(Router);

  /** For the password manager only: it needs the account name next to a password it saves or updates. */
  readonly $username = computed(() => this.auth.$user()?.username ?? '');

  private readonly $currentField = viewChild.required<UiInputComponent>('current');
  private readonly $newField = viewChild.required<UiInputComponent>('newPassword');

  readonly minLength = MIN_PASSWORD_LENGTH;
  readonly maxLength = MAX_PASSWORD_LENGTH;
  readonly form = createChangePasswordForm();

  $busy = this.auth.changePassword.$isRunning;
  private readonly $error = computed<ApiError | undefined>(
    () => this.auth.changePassword.$operation().error
  );
  /** A failure that belongs to no field: the network, a rate limit, a server error, a locked account. */
  /**
   * The answer of a locked account: a 401 that carries the API's own sentence. A 401 without one (a refresh that failed on the
   * way) is not a lockout and gets the generic sentence; the session is ended and sign-in opens in that case.
   */
  private readonly $locked = computed(() => {
    const error = this.$error();
    return error?.kind === API_ERROR_KIND.Unauthorized && error.detail.length > 0;
  });
  $banner = computed(() => {
    const error = this.$error();
    if (error === undefined || error.kind === API_ERROR_KIND.Validation) {
      return undefined;
    }
    return this.$locked()
      ? this.i18n.t('account.password.error.locked')
      : (error.message ?? this.i18n.t('error.generic'));
  });
  $bannerKind = computed(() => (this.$locked() ? 'danger' : 'warning'));

  constructor() {
    // A refused request puts its sentence at the field it is about, and the focus goes there.
    effect(() => {
      const error = this.$error();
      if (error?.kind === API_ERROR_KIND.Validation) {
        untracked(() => this.showFieldError(error));
      }
    });
  }

  /** An error of an earlier visit lives in the singleton service: a new page starts clean. */
  ngOnInit(): void {
    this.auth.changePassword.reset();
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (!this.$busy()) {
        this.auth.changePassword.reset();
      }
    });
    afterNextRender(() => this.$currentField().focus(), { injector: this.injector });
  }

  onVisibility(): void {
    if (document.visibilityState === 'hidden') {
      this.hideBoth();
    }
  }

  onSubmit(): void {
    if (this.$busy()) {
      return;
    }
    this.auth.changePassword.reset();
    this.hideBoth();
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      // The focus goes to the first field with an error, so a keyboard user does not have to look for it.
      this.focusInvalid();
      return;
    }
    this.auth.changePassword
      .run(this.form.getRawValue())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe();
  }

  onCancel(): void {
    void this.router.navigateByUrl(HOME_ROUTE);
  }

  /**
   * A 400 with field errors (the API repeating a client check) goes to those fields. A 400 with none is the wrong current
   * password: our sentence, translated, goes under that field (the API's own is English and may be reworded).
   */
  private showFieldError(error: ApiError): void {
    if (Object.keys(error.fieldErrors).length > 0) {
      const unmatched = applyFieldErrors(this.form, error);
      if (unmatched.length > 0) {
        this.form.controls.newPassword.setErrors({ [SERVER_ERROR_KEY]: unmatched[0] });
        this.form.controls.newPassword.markAsTouched();
      }
    } else {
      this.form.controls.currentPassword.setErrors({
        [SERVER_ERROR_KEY]: this.i18n.t('account.password.current.wrong')
      });
      this.form.controls.currentPassword.markAsTouched();
    }
    this.focusInvalid();
  }

  private focusInvalid(): void {
    afterNextRender(() => focusFirstInvalid(this.host.nativeElement), { injector: this.injector });
  }

  private hideBoth(): void {
    this.$currentField().hide();
    this.$newField().hide();
  }
}
