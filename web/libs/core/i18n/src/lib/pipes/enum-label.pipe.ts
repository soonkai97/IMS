import { Pipe, PipeTransform, inject } from '@angular/core';
import { MessageKey } from '../messages/message.types';
import { I18nService } from '../services/i18n.service';

/**  {{ row.status | enumLabel: statusLabels }}  Translates a server enum value through a label map (see enum-labels.ts). */
@Pipe({ name: 'enumLabel', pure: false })
export class EnumLabelPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: string | undefined, labels: Readonly<Record<string, MessageKey>>): string {
    return this.i18n.enumLabel(labels, value);
  }
}
