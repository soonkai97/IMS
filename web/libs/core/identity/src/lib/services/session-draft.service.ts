import { Injectable, inject } from '@angular/core';
import { AuthService } from './auth.service';
import { saveDraft, takeDraft } from './session-draft-storage';

/**
 * Lets a screen keep what the user had not sent when the session ends by expiry (AuthService.sessionExpiring$) and take it
 * back after the next sign-in. Drafts belong to the signed-in user and live in sessionStorage; they hold no state in memory, so
 * this service is safe at the root. The AuthService removes them at an explicit sign-out.
 */
@Injectable({ providedIn: 'root' })
export class SessionDraftService {
  private readonly auth = inject(AuthService);

  /** True when the draft was kept (a user is signed in and storage accepted it). */
  save(key: string, value: unknown): boolean {
    const user = this.auth.$user();
    return user !== undefined && saveDraft(user.id, key, value);
  }

  /** The draft of the signed-in user for this key, removed from storage; undefined when there is none. */
  take<T>(key: string): T | undefined {
    const user = this.auth.$user();
    return user === undefined ? undefined : takeDraft<T>(user.id, key);
  }
}
