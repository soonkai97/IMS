import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  isDevMode
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { I18nService, LanguageSelectComponent, TranslatePipe } from '@ims/core/i18n';
import { AuthService, HOME_ROUTE, SESSION_EXPIRED_REASON } from '@ims/core/identity';
import { API_ERROR_KIND, HTTP_STATUS, MAX_PASSWORD_LENGTH, MAX_USERNAME_LENGTH } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiFormFieldComponent,
  UiInputComponent
} from '@ims/ui';

/** Only same-app paths are accepted as a return URL, so a crafted link cannot send the user to another site. */
function safeReturnUrl(url: string | undefined): string {
  if (!url?.startsWith('/') || url.startsWith('//')) {
    return HOME_ROUTE;
  }
  return url;
}

@Component({
  selector: 'app-login-page',
  imports: [
    ReactiveFormsModule,
    LanguageSelectComponent,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiFormFieldComponent,
    UiInputComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly router = inject(Router);

  $returnUrl = input<string | undefined>(undefined, { alias: 'returnUrl' });
  $reason = input<string | undefined>(undefined, { alias: 'reason' });

  readonly maxUsernameLength = MAX_USERNAME_LENGTH;
  readonly maxPasswordLength = MAX_PASSWORD_LENGTH;
  readonly form = new FormGroup({
    username: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_USERNAME_LENGTH)]
    }),
    password: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(MAX_PASSWORD_LENGTH)]
    })
  });

  $isRunning = this.auth.login.$isRunning;
  $sessionExpired = computed(() => this.$reason() === SESSION_EXPIRED_REASON);
  /** The API answers every failed sign-in with the same sentence (unknown user, wrong password, locked). */
  $errorMessage = computed(() => {
    const error = this.auth.login.$operation().error;
    if (!error) {
      return undefined;
    }
    if (error.status === HTTP_STATUS.TooManyRequests) {
      return this.i18n.t('error.tooManyRequests');
    }
    return error.kind === API_ERROR_KIND.Unauthorized
      ? this.i18n.t('login.error.invalid')
      : error.detail;
  });
  readonly isDev = isDevMode();

  onSubmit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }
    this.auth.login
      .run(this.form.getRawValue())
      .pipe(
        tap(() => void this.router.navigateByUrl(safeReturnUrl(this.$returnUrl()))),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }
}
