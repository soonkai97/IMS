import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  OnInit,
  afterNextRender,
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
  UiFormLegendComponent,
  UiInputComponent
} from '@ims/ui';

const ACCOUNT_LOCKED_CODE = 'account_locked';

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
    UiFormLegendComponent,
    UiInputComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
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
    // The API's sentence for a locked account ("Try again in N minutes.") and for the rate limit names the wait.
    if (error.extensions['code'] === ACCOUNT_LOCKED_CODE && error.detail.length > 0) {
      return error.detail;
    }
    if (error.status === HTTP_STATUS.TooManyRequests) {
      return error.detail.length > 0 ? error.detail : this.i18n.t('error.tooManyRequests');
    }
    return error.kind === API_ERROR_KIND.Unauthorized
      ? this.i18n.t('login.error.invalid')
      : error.detail;
  });
  readonly isDev = isDevMode();

  /**
   * D3: a sign-in error lives only while the form is untouched. The failure sits in the singleton AuthService, so a
   * new page starts clean, and any edit of either field removes it. Nothing about it is written to storage.
   */
  ngOnInit(): void {
    this.auth.login.reset();
    this.form.valueChanges
      .pipe(
        // Not while a request runs: a late autofill must not unlock the running sign-in or hide its answer.
        tap(() => {
          if (!this.$isRunning()) {
            this.auth.login.reset();
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  onSubmit(): void {
    if (this.$isRunning()) {
      return;
    }
    // A new attempt removes the old banner first, so an invalid resubmit shows only the field messages (D3).
    this.auth.login.reset();
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      // The focus goes to the first field with an error, so a keyboard user does not have to look for it (EA-06).
      afterNextRender(
        () => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(),
        { injector: this.injector }
      );
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
