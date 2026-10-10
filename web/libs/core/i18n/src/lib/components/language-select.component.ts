import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { tap } from 'rxjs';
import { SelectOption, SelectValue, UiFormFieldComponent, UiSelectComponent } from '@ims/ui';
import { LANGUAGES, isLanguageCode } from '../constants/languages.const';
import { TranslatePipe } from '../pipes/translate.pipe';
import { I18nService } from '../services/i18n.service';

/** Labelled language picker (English / 中文 / Bahasa Melayu). Applies and stores the choice immediately. */
@Component({
  selector: 'app-language-select',
  imports: [ReactiveFormsModule, TranslatePipe, UiFormFieldComponent, UiSelectComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <app-form-field [label]="'language.label' | t">
      <app-select [formControl]="control" [options]="options" />
    </app-form-field>
  `
})
export class LanguageSelectComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  readonly control = new FormControl<SelectValue>(this.i18n.$language());
  readonly options: readonly SelectOption[] = LANGUAGES.map(language => ({
    value: language.code,
    label: language.label
  }));

  ngOnInit(): void {
    this.control.valueChanges
      .pipe(
        tap(value => {
          if (isLanguageCode(value)) {
            void this.i18n.setLanguage(value);
          }
        }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();
  }
}
