import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ConfirmHostComponent } from '@ims/core/notification';

// The toast host lives in the shell and on the sign-in page: it is the first thing in the page, not an overlay on top of it.
@Component({
  selector: 'app-root',
  imports: [ConfirmHostComponent, RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <router-outlet />
    <app-confirm-host />
  `
})
export class App {}
