import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  OnInit,
  inject,
  input
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { tap } from 'rxjs';
import {
  SelectValue,
  UiFormFieldComponent,
  UiRadioGroupComponent,
  UiSelectComponent
} from '@ims/ui';
import { LANGUAGES, isLanguageCode } from '../constants/languages.const';
import { TranslatePipe } from '../pipes/translate.pipe';
import { I18nService } from '../services/i18n.service';

/**
 * Language picker (English / 中文 / Bahasa Melayu). Applies and stores the choice immediately. `select` is a labelled dropdown;
 * `segmented` is one track with the three names always in view (the sign-in page, where there is no menu to open), the group
 * named "Language" for assistive technology.
 */
@Component({
  selector: 'app-language-select',
  imports: [
    ReactiveFormsModule,
    TranslatePipe,
    UiFormFieldComponent,
    UiRadioGroupComponent,
    UiSelectComponent
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if ($variant() === 'segmented') {
      <app-radio-group
        variant="segmented"
        [hideLegend]="true"
        [legend]="'language.label' | t"
        [options]="options"
        [formControl]="control"
      />
    } @else {
      <app-form-field [label]="'language.label' | t">
        <app-select [formControl]="control" [options]="options" />
      </app-form-field>
    }
  `
})
export class LanguageSelectComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly i18n = inject(I18nService);

  $variant = input<'select' | 'segmented'>('select', { alias: 'variant' });

  readonly control = new FormControl<SelectValue>(this.i18n.$language());
  /** Plain `{ value, label }` pairs: both the dropdown and the segmented group take them. */
  readonly options = LANGUAGES.map(language => ({
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
