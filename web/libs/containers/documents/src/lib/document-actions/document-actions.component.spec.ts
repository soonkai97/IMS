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
    spectator
      .queryAll('button')
      .map(button => button.getAttribute('aria-label') ?? (button.textContent ?? '').trim());

  it('renders nothing when the server allows nothing (read-only document)', () => {
    render([]);

    expect(labels()).toEqual([]);
  });

  it('draft: Save draft, Cancel document and Submit, but never Approve or Post', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit, DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document', 'More actions', 'Save draft', 'Submit']);
  });

  it('NV-10: on a phone Cancel moves into the more sheet when there is something else to do', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit, DOCUMENT_ACTION.Cancel]);
    const emitted: DocumentAction[] = [];
    spectator.output('$triggered').subscribe(action => emitted.push(action as DocumentAction));

    expect(spectator.query('.document-actions-cancel')?.classList).toContain('--overflow');
    spectator.click(spectator.query('.document-actions-more button') as HTMLElement);
    spectator.detectChanges();
    const sheetButton = spectator.query(
      'dialog button.ui-button-control.--danger, dialog .--danger button'
    );
    expect(sheetButton?.textContent).toContain('Cancel document');
    spectator.click(sheetButton as HTMLElement);

    expect(emitted).toEqual([DOCUMENT_ACTION.Cancel]);
  });

  it('NV-10: Cancel stays where it is when it is the only action (the submitter recalling)', () => {
    render([DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document']);
    expect(spectator.query('.document-actions-cancel')?.classList).not.toContain('--overflow');
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

  it('R28: Edit shows only when recall is listed, left of Post and secondary', () => {
    render([DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document', 'More actions', 'Edit', 'Post']);
    const edit = spectator
      .queryAll('app-button')
      .find(button => button.textContent?.includes('Edit'));
    expect(edit?.className).toContain('--secondary');

    render([DOCUMENT_ACTION.Post, DOCUMENT_ACTION.Cancel]);
    expect(labels()).not.toContain('Edit');
  });

  it('R28: a document that only waits for approval has Edit as its primary button, Cancel in the sheet', () => {
    render([DOCUMENT_ACTION.Recall, DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document', 'More actions', 'Edit']);
    expect(spectator.query('.document-actions-cancel')?.classList).toContain('--overflow');
    const edit = spectator
      .queryAll('app-button')
      .find(button => button.textContent?.includes('Edit'));
    expect(edit?.className).toContain('--primary');
  });

  it('R28: Edit emits recall and shows the spinner while it runs', () => {
    render([DOCUMENT_ACTION.Recall]);
    const emitted: DocumentAction[] = [];
    spectator.output('$triggered').subscribe(action => emitted.push(action as DocumentAction));

    spectator.click(spectator.query('button') as HTMLElement);
    expect(emitted).toEqual([DOCUMENT_ACTION.Recall]);

    spectator.setInput({ busy: true, busyAction: DOCUMENT_ACTION.Recall } as never);
    spectator.detectChanges();
    expect(spectator.query('button')?.getAttribute('aria-busy')).toBe('true');
  });

  it('R31: the reviewer of an approved Large document (cannot post) sees Cancel document alone', () => {
    render([DOCUMENT_ACTION.Cancel]);

    expect(labels()).toEqual(['Cancel document']);
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
    // Focusable (not natively disabled) and tied to the reason, so a keyboard user learns why (EA-22).
    expect(approve?.hasAttribute('disabled')).toBe(false);
    expect(approve?.getAttribute('aria-disabled')).toBe('true');
    const reasonId = approve?.getAttribute('aria-describedby') ?? '';
    expect(spectator.query(`#${reasonId}`)?.textContent).toContain("can't approve it");
  });

  it('a blocked Approve does nothing when pressed', () => {
    render([DOCUMENT_ACTION.Cancel], { selfApprovalBlocked: true });
    const emitted: DocumentAction[] = [];
    spectator.output('$triggered').subscribe(action => emitted.push(action as DocumentAction));

    spectator.click(
      spectator
        .queryAll('button')
        .find(button => button.textContent?.includes('Approve')) as HTMLElement
    );

    expect(emitted).toEqual([]);
  });

  it('NV-21: only the tapped button shows the spinner', () => {
    render([DOCUMENT_ACTION.Edit, DOCUMENT_ACTION.Submit]);
    spectator.setInput({ busy: true, busyAction: DOCUMENT_ACTION.Edit } as never);
    spectator.detectChanges();

    const spinning = spectator
      .queryAll('button')
      .filter(button => button.getAttribute('aria-busy') === 'true')
      .map(button => button.textContent ?? '');
    expect(spinning).toHaveLength(1);
    expect(spinning[0]).toContain('Save draft');
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
