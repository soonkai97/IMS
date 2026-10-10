import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { DOCUMENT_ACTION, DocumentAction } from '@ims/models';
import { DocumentActionsComponent } from './document-actions.component';

describe('DocumentActionsComponent (buttons come from the server allowedActions)', () => {
  let spectator: Spectator<DocumentActionsComponent>;
  const createComponent = createComponentFactory({
    component: DocumentActionsComponent,
    detectChanges: false
  });

  function render(
    allowedActions: string[],
    inputs: { canPost?: boolean; selfApprovalBlocked?: boolean; submitDisabled?: boolean } = {}
  ): void {
    spectator = createComponent({
      // Spectator types props by property name ($canPost) but Angular binds the alias (canPost).
      props: {
        allowedActions,
        canPost: inputs.canPost ?? false,
        selfApprovalBlocked: inputs.selfApprovalBlocked ?? false,
        submitDisabled: inputs.submitDisabled ?? false
      } as never
    });
    spectator.detectChanges();
  }

  const labels = (): string[] =>
    spectator.queryAll('button').map(button => (button.textContent ?? '').trim());

  it('renders nothing when the server allows nothing (read-only document)', () => {
    render([]);

    expect(labels()).toEqual([]);
  });

  it('draft: Save draft, Cancel document and Submit, but never Approve or Post', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit, DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document', 'Save draft', 'Submit']);
  });

  it('PS-08: a stale copy locks every button until it is reloaded', () => {
    render([DOCUMENT_ACTION.Approve, DOCUMENT_ACTION.Reject]);
    spectator.setInput({ locked: true } as never);
    spectator.detectChanges();

    const disabled = spectator.queryAll('button').map(button => button.hasAttribute('disabled'));
    expect(disabled.length).toBeGreaterThan(0);
    expect(disabled.every(Boolean)).toBe(true);
  });

  it('labels the single submit button "Submit & post" for roles that may post (D4)', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit], { canPost: true });

    expect(labels()).toContain('Submit & post');
  });

  it('submitted and awaiting approval: Approve and Reject only', () => {
    render([DOCUMENT_ACTION.Approve, DOCUMENT_ACTION.Reject]);

    expect(labels()).toEqual(['Reject', 'Approve']);
  });

  it('approved: Post; posted: Reverse', () => {
    render([DOCUMENT_ACTION.Post]);
    expect(labels()).toEqual(['Post']);

    render([DOCUMENT_ACTION.Reverse]);
    expect(labels()).toEqual(['Reverse']);
  });

  it('ignores action names it does not know', () => {
    render(['teleport', DOCUMENT_ACTION.Post]);

    expect(labels()).toEqual(['Post']);
  });

  it('shows Approve disabled with the reason when self-approval is the only blocker (E3)', () => {
    render([DOCUMENT_ACTION.Cancel], { selfApprovalBlocked: true });

    const approve = spectator
      .queryAll('button')
      .find(button => button.textContent?.includes('Approve'));
    expect(approve?.hasAttribute('disabled')).toBe(true);
    expect(spectator.element.textContent).toContain("can't approve it");
  });

  it('disables Submit while lines are short on stock', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit], { submitDisabled: true });

    const submit = spectator
      .queryAll('button')
      .find(button => button.textContent?.includes('Submit'));
    expect(submit?.hasAttribute('disabled')).toBe(true);
  });

  it('emits the action that was clicked', () => {
    render([DOCUMENT_ACTION.Post]);
    const emitted: DocumentAction[] = [];
    spectator.output('$triggered').subscribe(action => emitted.push(action as DocumentAction));

    spectator.click(spectator.query('button') as HTMLElement);

    expect(emitted).toEqual([DOCUMENT_ACTION.Post]);
  });
});
