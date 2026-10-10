import { createHostFactory, SpectatorHost } from '@ngneat/spectator/jest';
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
