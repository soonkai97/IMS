import { Pipe, PipeTransform, inject } from '@angular/core';
import { MessageParams } from '../messages/message-format';
import { MessageKey } from '../messages/message.types';
import { I18nService } from '../services/i18n.service';

/**  {{ 'doc.post.title' | t: { docNo: doc.no } }}  Impure on purpose: it must re-run when the language signal changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: MessageKey, params?: MessageParams): string {
    return this.i18n.t(key, params);
  }
}
