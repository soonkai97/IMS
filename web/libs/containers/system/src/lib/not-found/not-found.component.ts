import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '@ims/core/i18n';
import { UiEmptyStateComponent } from '@ims/ui';

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink, TranslatePipe, UiEmptyStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './not-found.component.html',
  styleUrl: './not-found.component.scss'
})
export class NotFoundPageComponent {}
