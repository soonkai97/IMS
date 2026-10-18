import { createHostFactory, SpectatorHost } from '@ngneat/spectator/jest';
import { UiModalState } from '../../services/modal-state.service';
import { UiDialogComponent } from './dialog.component';

describe('UiDialogComponent focus', () => {
  let spectator: SpectatorHost<UiDialogComponent, { open: boolean }>;
  const createHost = createHostFactory({ component: UiDialogComponent });

  async function open(template: string): Promise<void> {
    spectator = createHost(template, { hostProps: { open: true } });
    spectator.detectChanges();
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  it('focuses the field marked autofocus once the content exists (NV-03: a scanner can type at once)', async () => {
    await open(
      `<app-dialog [(open)]="open" heading="Add item"><input id="search" data-autofocus /></app-dialog>`
    );

    expect(document.activeElement?.id).toBe('search');
  });

  it('does not look inside a wrapper component: only the native control takes the focus', async () => {
    await open(
      `<app-dialog [(open)]="open" heading="Add item">
         <div data-autofocus id="wrapper"><input id="inner" data-autofocus /></div>
       </app-dialog>`
    );

    expect(document.activeElement?.id).toBe('inner');
  });

  it('leaves the focus alone when nothing asks for it', async () => {
    await open(`<app-dialog [(open)]="open" heading="Info"><p>Hello</p></app-dialog>`);

    expect(document.activeElement?.tagName).not.toBe('INPUT');
  });
});

describe('UiDialogComponent modal state', () => {
  let spectator: SpectatorHost<UiDialogComponent, { open: boolean }>;
  const createHost = createHostFactory({ component: UiDialogComponent });

  it('counts as an open modal only while it is open, so the page copy of the toast host steps aside', () => {
    spectator = createHost(`<app-dialog [(open)]="open" heading="Info"><p>Hello</p></app-dialog>`, {
      hostProps: { open: true }
    });
    const modal = spectator.inject(UiModalState);
    spectator.detectChanges();

    expect(modal.$isOpen()).toBe(true);

    spectator.component.$open.set(false);
    spectator.detectChanges();

    expect(modal.$isOpen()).toBe(false);
  });
});

describe('UiDialogComponent naming and long values (R13)', () => {
  let spectator: SpectatorHost<UiDialogComponent, { open: boolean; heading: string; text: string }>;
  const createHost = createHostFactory({ component: UiDialogComponent });
  const LONG = `Delete ${'W'.repeat(500)}`;

  function create(open: boolean): void {
    spectator = createHost(
      `<app-dialog [(open)]="open" [heading]="heading"><p class="body">{{ text }}</p></app-dialog>`,
      { hostProps: { open, heading: LONG, text: 'W'.repeat(500) } }
    );
    spectator.detectChanges();
  }

  async function createAndSettle(): Promise<void> {
    create(true);
    await spectator.fixture.whenStable();
    spectator.detectChanges();
  }

  it('has no aria-labelledby while closed, so nothing dangles (ER-20)', () => {
    create(false);

    const dialog = spectator.query('dialog');
    expect(dialog?.hasAttribute('aria-labelledby')).toBe(false);
    expect(spectator.query('.ui-dialog-title')).toBeNull();
  });

  it('names itself by its title while open', () => {
    create(true);

    const dialog = spectator.query('dialog');
    const title = spectator.query('.ui-dialog-title');
    expect(title?.id).toBeTruthy();
    expect(dialog?.getAttribute('aria-labelledby')).toBe(title?.id);
  });

  it('shows a 500-character heading and body whole, never cut or titled', async () => {
    await createAndSettle();

    const title = spectator.query('.ui-dialog-title');
    expect(title?.textContent?.trim()).toBe(LONG);
    expect(title?.hasAttribute('title')).toBe(false);
    expect(spectator.query('.ui-dialog-body')?.textContent?.trim().length).toBe(500);
  });

  it('closes from the close button and again removes the name', () => {
    create(true);

    spectator.click('.ui-dialog-header button');

    expect(spectator.hostComponent.open).toBe(false);
    expect(spectator.query('dialog')?.hasAttribute('aria-labelledby')).toBe(false);
  });
});
