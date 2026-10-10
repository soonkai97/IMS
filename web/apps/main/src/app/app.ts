import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHostComponent, ToastHostComponent } from '@ims/core/notification';

@Component({
  selector: 'app-root',
  imports: [ConfirmHostComponent, RouterOutlet, ToastHostComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <app-toast-host />
    <app-confirm-host />
  `
})
export class App {}
