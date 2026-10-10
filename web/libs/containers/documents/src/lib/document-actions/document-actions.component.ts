import {
  ChangeDetectionStrategy,
  Component,
  booleanAttribute,
  computed,
  input,
  output
} from '@angular/core';
import { MessageKey, TranslatePipe } from '@ims/core/i18n';
import { DOCUMENT_ACTION, DocumentAction, toDocumentActions } from '@ims/models';
import { UiButtonComponent, UiIconComponent } from '@ims/ui';

/**
 * The buttons of a document, taken ONLY from the server's `allowedActions`: a button that is not listed is not
 * rendered. The one exception is Approve, shown disabled with a reason when self-approval is the only blocker (E3).
 */
@Component({
  selector: 'app-document-actions',
  imports: [TranslatePipe, UiButtonComponent, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './document-actions.component.html',
  styleUrl: './document-actions.component.scss'
})
export class DocumentActionsComponent {
  $allowedActions = input.required<readonly string[]>({ alias: 'allowedActions' });
  /** Roles that may post get the single "Submit & post" button; others only "Submit" (D4). */
  $canPost = input(false, { alias: 'canPost', transform: booleanAttribute });
  $busy = input(false, { alias: 'busy', transform: booleanAttribute });
  /** The opened copy is out of date (E4): nothing can be done with it until it is reloaded. */
  $locked = input(false, { alias: 'locked', transform: booleanAttribute });
  $selfApprovalBlocked = input(false, {
    alias: 'selfApprovalBlocked',
    transform: booleanAttribute
  });
  /** Disables Submit (e.g. lines short on stock); the page shows the reason next to the buttons. */
  $submitDisabled = input(false, { alias: 'submitDisabled', transform: booleanAttribute });

  $triggered = output<DocumentAction>({ alias: 'triggered' });

  readonly action = DOCUMENT_ACTION;

  private readonly $actions = computed(() => toDocumentActions(this.$allowedActions()));

  $inactive = computed(() => this.$busy() || this.$locked());
  $canSave = computed(() => this.$actions().has(DOCUMENT_ACTION.Edit));
  $canSubmit = computed(() => this.$actions().has(DOCUMENT_ACTION.Submit));
  $canApprove = computed(() => this.$actions().has(DOCUMENT_ACTION.Approve));
  $canReject = computed(() => this.$actions().has(DOCUMENT_ACTION.Reject));
  $canCancel = computed(() => this.$actions().has(DOCUMENT_ACTION.Cancel));
  $canPostDocument = computed(() => this.$actions().has(DOCUMENT_ACTION.Post));
  $canReverse = computed(() => this.$actions().has(DOCUMENT_ACTION.Reverse));
  $showBlockedApprove = computed(() => this.$selfApprovalBlocked() && !this.$canApprove());
  $submitLabelKey = computed<MessageKey>(() =>
    this.$canPost() ? 'doc.action.submitAndPost' : 'doc.action.submit'
  );

  onTrigger(action: DocumentAction): void {
    this.$triggered.emit(action);
  }
}
