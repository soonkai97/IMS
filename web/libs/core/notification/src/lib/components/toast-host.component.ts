import { NgTemplateOutlet } from '@angular/common';
import {
  afterNextRender,
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  ElementRef,
  inject,
  Injector,
  input,
  untracked
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router } from '@angular/router';
import { filter } from 'rxjs';
import { UiModalState, UiToastComponent } from '@ims/ui';
import { Toast, ToastService } from '../services/toast.service';

/**
 * In the shell and on the sign-in page. Never takes focus; Escape dismisses the focused toast. While a modal dialog is open the
 * dialog renders its own copy (`inDialog`) and this one renders nothing: one live node at a time.
 */
@Component({
  selector: 'app-toast-host',
  imports: [NgTemplateOutlet, UiToastComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './toast-host.component.html',
  styleUrl: './toast-host.component.scss',
  host: { '[class.--in-dialog]': '$inDialog()', '[hidden]': '$hidden()' }
})
export class ToastHostComponent {
  private readonly toastService = inject(ToastService);
  private readonly modalState = inject(UiModalState);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
  private readonly injector = inject(Injector);
  private newestError = 0;
  private wasModal = false;

  $inDialog = input(false, { alias: 'inDialog' });
  $hidden = computed(() => !this.$inDialog() && this.modalState.$isOpen());

  $errors = computed(() => this.toastService.$toasts().filter(toast => toast.kind === 'error'));
  $messages = computed(() => this.toastService.$toasts().filter(toast => toast.kind !== 'error'));

  /** A new error on a scrolled page: bring the in-flow banner into view (never the focus), or a sighted user sees nothing. */
  private readonly revealNewError = effect(() => {
    const newest = Math.max(0, ...this.$errors().map(toast => toast.id));
    if (newest > this.newestError) {
      this.newestError = newest;
      afterNextRender(() => this.reveal(), { injector: this.injector });
    }
  });

  /** A dialog that closes takes the errors it showed with it, so the page copy does not announce them a second time. */
  private readonly clearOnModalClose = effect(() => {
    const open = this.modalState.$isOpen();
    if (this.wasModal && !open && !this.$inDialog()) {
      untracked(() => this.toastService.dismissErrors());
    }
    this.wasModal = open;
  });

  constructor() {
    inject(Router)
      .events.pipe(
        filter(event => event instanceof NavigationStart),
        takeUntilDestroyed(inject(DestroyRef))
      )
      .subscribe(() => this.toastService.dismissErrors());
  }

  private reveal(): void {
    const element = this.element;
    if (element.hidden || getComputedStyle(element).position === 'fixed') {
      return;
    }
    const still =
      window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ||
      document.documentElement.dataset['motion'] === 'off';
    element.scrollIntoView?.({ block: 'nearest', behavior: still ? 'auto' : 'smooth' });
  }

  onDismiss(toast: Toast): void {
    this.toastService.dismiss(toast.id);
  }

  onAction(toast: Toast): void {
    toast.action?.run();
    this.toastService.dismiss(toast.id);
  }

  /** The pointer or focus is on a toast: it is being read, so its timer stops. */
  onHold(toast: Toast): void {
    this.toastService.pause(toast.id);
  }

  /** The timer runs again only when neither the pointer nor focus is left on the toast. */
  onRelease(toast: Toast, event: Event): void {
    const element = event.currentTarget as HTMLElement;
    if (!element.matches(':hover') && !element.contains(element.ownerDocument.activeElement)) {
      this.toastService.resume(toast.id);
    }
  }
}
