import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild
} from '@angular/core';
import { toClassStr } from '@ims/utils';
import { UI_TEXT } from '../../tokens/ui-text.token';
import { UiButtonComponent } from '../button/button.component';
import { UiIconComponent } from '../icon/icon.component';

/** File picker with drag-and-drop. Validation of type and size is the caller's job (it knows the copy). */
@Component({
  selector: 'app-file-input',
  imports: [UiButtonComponent, UiIconComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './file-input.component.html',
  styleUrl: './file-input.component.scss',
  host: { '[class]': '$hostClass()' }
})
export class UiFileInputComponent {
  private readonly uiText = inject(UI_TEXT);

  $accept = input('', { alias: 'accept' });
  $label = input.required<string>({ alias: 'label' });

  $fileSelected = output<File>({ alias: 'fileSelected' });

  private readonly $native = viewChild.required<ElementRef<HTMLInputElement>>('native');

  $dragging = signal(false);
  $text = computed(() => this.uiText());
  $hostClass = computed(() => toClassStr(['ui-file-input', this.$dragging() && '--dragging']));

  onChoose(): void {
    this.$native().nativeElement.click();
  }

  onFileChange(event: Event): void {
    if (!(event.target instanceof HTMLInputElement)) {
      return;
    }
    const file = event.target.files?.item(0);
    event.target.value = '';
    if (file) {
      this.$fileSelected.emit(file);
    }
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.$dragging.set(true);
  }

  onDragLeave(): void {
    this.$dragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.$dragging.set(false);
    const file = event.dataTransfer?.files.item(0);
    if (file) {
      this.$fileSelected.emit(file);
    }
  }
}
