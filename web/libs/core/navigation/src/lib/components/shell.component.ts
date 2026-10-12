import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { tap } from 'rxjs';
import { LoadingService } from '@ims/core/http';
import { I18nService, LanguageSelectComponent, ROLE_LABELS, TranslatePipe } from '@ims/core/i18n';
import { AuthService, PermissionService } from '@ims/core/identity';
import { THEME_MODE, ThemeService, isThemeMode } from '@ims/core/theme';
import {
  RadioOption,
  UiButtonComponent,
  UiDialogComponent,
  UiIconComponent,
  UiRadioGroupComponent
} from '@ims/ui';
import { delayedLoading } from '@ims/utils';
import { NavigationService } from '../services/navigation.service';
import { PageTitleService } from '../services/page-title.service';

/**
 * App chrome. Phones (< 600px): top bar + bottom tab bar with a "More" sheet. Tablets: icon rail. Desktop (>= 1024px):
 * sidebar grouped by Documents / Stock / Master data / Insight / Admin (docs/design/mvp-ux.md 1.2).
 */
@Component({
  selector: 'app-shell',
  imports: [
    ReactiveFormsModule,
    LanguageSelectComponent,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    TranslatePipe,
    UiButtonComponent,
    UiDialogComponent,
    UiIconComponent,
    UiRadioGroupComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.scss'
})
export class ShellComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);
  private readonly loading = inject(LoadingService);
  private readonly navigation = inject(NavigationService);
  private readonly pageTitle = inject(PageTitleService);
  private readonly permissions = inject(PermissionService);
  private readonly theme = inject(ThemeService);

  readonly themeControl = new FormControl<string>(this.theme.$mode(), { nonNullable: true });

  $groups = this.navigation.$groups;
  $phoneTabs = this.navigation.$phoneTabs;
  $moreEntries = this.navigation.$moreEntries;
  $showProgress = delayedLoading(this.loading.$isLoading);
  $announcement = this.pageTitle.$announcement;
  $displayName = this.permissions.$displayName;
  $role = this.permissions.$role;

  $moreOpen = signal(false);
  $accountOpen = signal(false);

  $themeOptions = computed<readonly RadioOption[]>(() => [
    { value: THEME_MODE.System, label: this.i18n.t('theme.system') },
    { value: THEME_MODE.Light, label: this.i18n.t('theme.light') },
    { value: THEME_MODE.Dark, label: this.i18n.t('theme.dark') }
  ]);
  /** The visible text is the user's name (hidden on a phone): the accessible name starts with "Account" and contains it (WCAG 2.5.3). */
  $accountLabel = computed(() => `${this.i18n.t('shell.account')}, ${this.$displayName()}`);
  $roleLabel = computed(() => this.i18n.enumLabel(ROLE_LABELS, this.$role()));

  ngOnInit(): void {
    this.themeControl.valueChanges
      .pipe(
        tap(value => {
          if (isThemeMode(value)) {
            this.theme.setMode(value);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }

  onSkipToContent(event: Event, main: HTMLElement): void {
    event.preventDefault();
    main.focus();
  }

  onOpenMore(): void {
    this.$moreOpen.set(true);
  }

  onCloseMore(): void {
    this.$moreOpen.set(false);
  }

  onOpenAccount(): void {
    this.$accountOpen.set(true);
  }

  onSignOut(): void {
    this.$accountOpen.set(false);
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
  }
}
