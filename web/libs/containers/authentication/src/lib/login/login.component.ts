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
  input,
  isDevMode,
  signal,
  viewChild
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { I18nService, LanguageSelectComponent, TranslatePipe } from '@ims/core/i18n';
import { AuthService, HOME_ROUTE, SESSION_EXPIRED_REASON } from '@ims/core/identity';
import { ToastHostComponent } from '@ims/core/notification';
import { MAX_PASSWORD_LENGTH, MAX_USERNAME_LENGTH } from '@ims/models';
import {
  UiBannerComponent,
  UiButtonComponent,
  UiFormFieldComponent,
  UiFormLegendComponent,
  UiIconComponent,
  UiInputComponent,
  UiLogoComponent
} from '@ims/ui';
import { loginFailureOf } from './login-failure';
import { LOGIN_FAILURE, LoginFailure } from './login-failure.types';

/** From this many seconds the rate-limit wait is told in minutes ("90 seconds" reads better as "2 minutes"). */
const MINUTES_FROM_SECONDS = 90;
const SECONDS_PER_MINUTE = 60;

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
    ToastHostComponent,
    TranslatePipe,
    UiBannerComponent,
    UiButtonComponent,
    UiFormFieldComponent,
    UiFormLegendComponent,
    UiIconComponent,
    UiInputComponent,
    UiLogoComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
  host: { '(window:online)': 'onConnection(true)', '(window:offline)': 'onConnection(false)' }
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
  private readonly $username = viewChild.required<UiInputComponent>('username');

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

  /** After a good answer the page stays busy until the next page has opened, so a second Enter cannot sign in twice. */
  private readonly $navigating = signal(false);
  $isRunning = this.auth.login.$isRunning;
  $busy = computed(() => this.$isRunning() || this.$navigating());
  $sessionExpired = computed(() => this.$reason() === SESSION_EXPIRED_REASON);
  /** The failure as data; the sentence is made from it below, so a language change rewrites the banner. */
  $failure = computed<LoginFailure | undefined>(() => {
    const error = this.auth.login.$operation().error;
    return error ? loginFailureOf(error) : undefined;
  });
  $failureMessage = computed(() => {
    const failure = this.$failure();
    return failure ? this.messageOf(failure) : undefined;
  });
  /** Danger: the input or the account is the cause. Warning: the environment is (rate limit, network, server). */
  $failureKind = computed(() => {
    const kind = this.$failure()?.kind;
    return kind === LOGIN_FAILURE.Invalid || kind === LOGIN_FAILURE.Locked ? 'danger' : 'warning';
  });
  /** The browser's own flag is unreliable, so it only adds a notice; the button is never blocked by it. */
  private readonly $online = signal(navigator.onLine);
  $offlineNotice = computed(() => !this.$online() && this.$failure() === undefined);
  /** One shake per failed attempt; the error text and the field state carry the message when motion is off. */
  $shaking = signal(false);
  $aboutLabel = computed(() => this.i18n.t('login.about', { name: this.i18n.t('app.name') }));
  readonly isDev = isDevMode();

  constructor() {
    effect(() => {
      if (this.$failure()) {
        this.$shaking.set(true);
        // The focus stays where it was (moving it would cut off the announcement); the password is selected so typing replaces it.
        afterNextRender(() => this.selectPassword(), { injector: this.injector });
      }
    });
  }

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
          if (!this.$busy()) {
            this.auth.login.reset();
            this.$shaking.set(false);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
    // The user came here to type: the first field has the focus from the first frame (the entrance never blocks input).
    afterNextRender(() => this.$username().focus(), { injector: this.injector });
  }

  onConnection(online: boolean): void {
    this.$online.set(online);
  }

  onSubmit(): void {
    if (this.$busy()) {
      return;
    }
    // A new attempt removes the old banner first, so an invalid resubmit shows only the field messages (D3).
    this.auth.login.reset();
    this.$shaking.set(false);
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
        tap(() => {
          this.$navigating.set(true);
          void this.router
            .navigateByUrl(safeReturnUrl(this.$returnUrl()))
            .finally(() => this.$navigating.set(false));
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  private messageOf(failure: LoginFailure): string {
    switch (failure.kind) {
      case LOGIN_FAILURE.Invalid:
        return this.i18n.t('login.error.invalid');
      case LOGIN_FAILURE.Locked:
        return this.i18n.t('login.error.locked', { minutes: failure.minutes ?? 0 });
      case LOGIN_FAILURE.RateLimited:
        return this.rateLimitMessage(failure.seconds);
      case LOGIN_FAILURE.Network:
        return this.i18n.t('error.network');
      default:
        return this.i18n.t('login.error.server');
    }
  }

  /** The wait is told in seconds, or in minutes (rounded up) from 90 seconds. A 429 without a number gets the plain sentence. */
  private rateLimitMessage(seconds: number | undefined): string {
    if (seconds === undefined) {
      return this.i18n.t('error.tooManyRequests');
    }
    return seconds >= MINUTES_FROM_SECONDS
      ? this.i18n.t('login.error.rateLimited.minutes', {
          minutes: Math.ceil(seconds / SECONDS_PER_MINUTE)
        })
      : this.i18n.t('login.error.rateLimited', { seconds });
  }

  private selectPassword(): void {
    const field = this.host.nativeElement.querySelector<HTMLInputElement>(
      'input[autocomplete="current-password"]'
    );
    if (field && document.activeElement === field) {
      field.select();
    }
  }
}
