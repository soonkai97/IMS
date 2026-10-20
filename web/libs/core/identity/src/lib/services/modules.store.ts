import { Injectable, inject, linkedSignal } from '@angular/core';
import { Observable, catchError, defer, finalize, map, of, shareReplay, tap } from 'rxjs';
import { ModuleDto, ModuleSwitchResult, ModulesClient, UserInfo } from '@ims/api';
import { handledInline } from '@ims/core/http';
import { ApiCommand, QueryLoader } from '@ims/utils';
import { RECHECK_QUIET_MS } from '../constants/identity.const';
import { AuthService } from './auth.service';

export interface SwitchModuleArgs {
  readonly code: string;
  readonly enabled: boolean;
  readonly reason: string;
}

/**
 * Which modules of the installation are on (ADR modules-and-tracking-dimensions 4.5). The core is always on and has no code.
 *  - `isEnabled(code)` starts from `UserInfo.modules`, which arrives with sign-in, every token refresh and /me, and is replaced at once
 *    by a fresher `GET /api/modules` (the admin screen, a switch, a 404 that may mean "turned off"). A new `UserInfo` starts again from
 *    its own list, so the menu is never older than the last refresh (about 5 minutes).
 *  - `list` is what the Settings > Modules screen shows (name, description, state, requires).
 * A module that is off hides its menu items and routes only; the API answers 404 for its endpoints whatever the screen shows.
 */
@Injectable({ providedIn: 'root' })
export class ModulesStore {
  private readonly client = inject(ModulesClient);
  private readonly auth = inject(AuthService);

  private readonly $enabledCodes = linkedSignal<UserInfo | undefined, ReadonlySet<string>>({
    source: this.auth.$user,
    computation: user => new Set(user?.modules ?? [])
  });

  private recheck: Observable<readonly ModuleDto[]> | undefined;
  private lastQuietCheck = Number.NEGATIVE_INFINITY;

  readonly list = new QueryLoader<void, ModuleDto[]>((_query, context) =>
    this.client.list(context).pipe(tap(modules => this.apply(modules)))
  );

  /**
   * Exclusive: a double tap sends one request. The screen shows every failure of the call in place, so none raises a toast. A
   * success reloads the list, so the screen and the menu show what the server holds now.
   */
  readonly switchModule = new ApiCommand<SwitchModuleArgs, ModuleSwitchResult>(
    args =>
      this.client
        .switch(args.code, { enabled: args.enabled, reason: args.reason }, handledInline())
        .pipe(tap(() => this.list.load(undefined, handledInline()))),
    true
  );

  /** True when the module is on. Reads a signal, so a menu, a guard or a template that asks follows a change at once. */
  isEnabled(code: string): boolean {
    return this.$enabledCodes().has(code);
  }

  /**
   * A 404 on an API call may be a turned-off module (its endpoints answer like an unknown address). Reads the list again and
   * returns the modules that were on and are now off (none: the 404 was something else). One request serves callers that ask together,
   * and a check that found nothing is not repeated for a few seconds (a barcode that matches nothing is a 404 too). Never fails.
   */
  checkAfterNotFound(): Observable<readonly ModuleDto[]> {
    if (Date.now() - this.lastQuietCheck < RECHECK_QUIET_MS) {
      return of([]);
    }
    this.recheck ??= defer(() => {
      const before = this.$enabledCodes();
      return this.client.list(handledInline()).pipe(
        tap(modules => {
          this.apply(modules);
          this.list.$data.set(modules);
        }),
        map(modules => modules.filter(module => !module.enabled && before.has(module.code))),
        catchError(() => of([] as ModuleDto[])),
        tap(turnedOff => {
          if (turnedOff.length === 0) {
            this.lastQuietCheck = Date.now();
          }
        })
      );
    }).pipe(
      finalize(() => (this.recheck = undefined)),
      shareReplay({ bufferSize: 1, refCount: false })
    );
    return this.recheck;
  }

  private apply(modules: readonly ModuleDto[]): void {
    this.$enabledCodes.set(new Set(modules.filter(module => module.enabled).map(m => m.code)));
  }
}
