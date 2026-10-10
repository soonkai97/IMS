import { Injectable, inject } from '@angular/core';
import { tap } from 'rxjs';
import {
  ApprovalRuleDto,
  ApprovalRuleSaveRequest,
  ApprovalRulesClient,
  SettingsClient,
  SettingsDto
} from '@ims/api';
import { SystemSettingsStore } from '@ims/core/settings';
import { ApiCommand, QueryLoader } from '@ims/utils';

export interface SaveRuleArgs {
  readonly id?: number;
  readonly body: ApprovalRuleSaveRequest;
}

/** Admin screen data: the switches (negative stock, self-approval) and the approval rules. */
@Injectable({ providedIn: 'root' })
export class SystemRulesStore {
  private readonly settingsClient = inject(SettingsClient);
  private readonly rulesClient = inject(ApprovalRulesClient);
  private readonly systemSettings = inject(SystemSettingsStore);

  readonly settings = new QueryLoader<void, SettingsDto>(() => this.settingsClient.get());
  readonly rules = new QueryLoader<void, ApprovalRuleDto[]>(() => this.rulesClient.list());

  readonly saveSettings = new ApiCommand<SettingsDto, SettingsDto>(body =>
    this.settingsClient.update(body).pipe(
      tap(saved => this.settings.$data.set(saved)),
      tap(saved => this.systemSettings.apply(saved))
    )
  );
  readonly saveRule = new ApiCommand<SaveRuleArgs, ApprovalRuleDto>(args =>
    (args.id === undefined
      ? this.rulesClient.create(args.body)
      : this.rulesClient.update(args.id, args.body)
    ).pipe(tap(() => this.rules.reload()))
  );
}
