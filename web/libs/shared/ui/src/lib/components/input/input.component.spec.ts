import { createComponentFactory, Spectator } from '@ngneat/spectator/jest';
import { UiInputComponent } from './input.component';

describe('UiInputComponent number entry (NR-01, NR-08)', () => {
  let spectator: Spectator<UiInputComponent>;
  const changes: (string | number | null)[] = [];
  const createComponent = createComponentFactory({
    component: UiInputComponent,
    detectChanges: false
  });

  function render(props: Record<string, unknown> = {}): HTMLInputElement {
    changes.length = 0;
    spectator = createComponent({ props: { type: 'number', ...props } as never });
    spectator.component.registerOnChange(value => changes.push(value));
    spectator.detectChanges();
    return spectator.query('input') as HTMLInputElement;
  }

  /** Counts the writes the component makes to the native value (a write while typing eats the caret). */
  function watchWrites(input: HTMLInputElement): jest.Mock {
    const writes = jest.fn();
    const native = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
    Object.defineProperty(input, 'value', {
      configurable: true,
      get: () => native?.get?.call(input) as string,
      set: (value: string) => {
        writes(value);
        native?.set?.call(input, value);
      }
    });
    return writes;
  }

  /** What the browser reports after the user typed: the text, and the model value after the input event. */
  function type(input: HTMLInputElement, reported: string): void {
    native(input, reported);
    spectator.dispatchFakeEvent(input, 'input');
    spectator.detectChanges();
  }

  function native(input: HTMLInputElement, value: string): void {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set?.call(input, value);
  }

  it('NR-01: entering a number field selects its value, so typing replaces it', () => {
    const input = render();
    spectator.component.writeValue(1);
    spectator.detectChanges();
    const select = jest.spyOn(input, 'select');

    spectator.dispatchFakeEvent(input, 'focus');

    expect(select).toHaveBeenCalledTimes(1);
  });

  it('NR-01: a plain text field does not select on focus', () => {
    const text = render({ type: 'text' });
    const textSelect = jest.spyOn(text, 'select');

    spectator.dispatchFakeEvent(text, 'focus');

    expect(textSelect).not.toHaveBeenCalled();
  });

  describe('selects again after the browser placed the caret (iOS, Android)', () => {
    beforeEach(() => jest.useFakeTimers());
    afterEach(() => jest.useRealTimers());

    it('NR-01: a second select follows the focus and the focusing tap, while the field is still focused', () => {
      const input = render();
      input.focus();
      jest.runAllTimers();
      const select = jest.spyOn(input, 'select');

      spectator.dispatchFakeEvent(input, 'focus');
      spectator.dispatchMouseEvent(input, 'mousedown');
      expect(select).toHaveBeenCalledTimes(1);
      jest.runAllTimers();

      expect(select).toHaveBeenCalledTimes(2);
    });

    it('NR-01: the late select is skipped once the user typed, and when the field lost the focus', () => {
      const input = render();
      input.focus();
      jest.runAllTimers();
      const select = jest.spyOn(input, 'select');

      spectator.dispatchFakeEvent(input, 'focus');
      type(input, '1');
      jest.runAllTimers();
      expect(select).toHaveBeenCalledTimes(1);

      spectator.dispatchFakeEvent(input, 'focus');
      input.blur();
      jest.runAllTimers();
      expect(select).toHaveBeenCalledTimes(2);
    });
  });

  it('NR-01: the mouseup of the tap that focused the field does not drop the selection; later taps place the caret', () => {
    const input = render();

    spectator.dispatchMouseEvent(input, 'mousedown');
    const tap = new MouseEvent('mouseup', { cancelable: true });
    input.dispatchEvent(tap);
    expect(tap.defaultPrevented).toBe(true);

    input.focus();
    spectator.dispatchMouseEvent(input, 'mousedown');
    const later = new MouseEvent('mouseup', { cancelable: true });
    input.dispatchEvent(later);
    expect(later.defaultPrevented).toBe(false);
  });

  it('NR-01: the value typed over the selection reaches the form as a number', () => {
    const input = render();
    spectator.component.writeValue(1);
    spectator.detectChanges();

    type(input, '12');

    expect(changes).toEqual([12]);
  });

  it('NR-08: typing 1.5 then Backspace never writes the value back, so the caret stays', () => {
    const input = render();
    const writes = watchWrites(input);

    type(input, '1.5');
    type(input, '1'); // the browser reports "1" for the text "1."
    type(input, '1');
    type(input, '');

    expect(writes).not.toHaveBeenCalled();
    expect(changes).toEqual([1.5, 1, 1, null]);
  });

  it('NR-08: "2." then "5" keeps the point, the field is not rewritten between the keys', () => {
    const input = render();
    const writes = watchWrites(input);

    type(input, '2');
    type(input, '2.5');

    expect(writes).not.toHaveBeenCalled();
    expect(changes).toEqual([2, 2.5]);
  });

  it('NR-08: on leaving the field the text is normalised: a lone sign is cleared', () => {
    const input = render();
    type(input, '');
    const writes = watchWrites(input);

    spectator.dispatchFakeEvent(input, 'blur');

    expect(writes).toHaveBeenCalledWith('');
  });

  it('on leaving the field text the browser accepts stays as typed (1.50 keeps its two decimals)', () => {
    const input = render();
    type(input, '1.50');

    spectator.dispatchFakeEvent(input, 'blur');

    expect(input.value).toBe('1.50');
    expect(changes).toEqual([1.5]);
  });

  it('a value set from outside replaces the text, an equal one is not written over what is being typed', () => {
    const input = render();
    type(input, '1');
    const writes = watchWrites(input);

    spectator.component.writeValue(1);
    expect(writes).not.toHaveBeenCalled();

    spectator.component.writeValue(5);
    expect(writes).toHaveBeenCalledWith('5');
    expect(input.value).toBe('5');

    spectator.component.writeValue(null);
    expect(input.value).toBe('');
  });

  it('an empty model clears text the browser holds back as invalid ("-"), e.g. after form.reset()', () => {
    const input = render();
    Object.defineProperty(input, 'validity', { value: { badInput: true } });
    const writes = watchWrites(input);

    spectator.component.writeValue(null);

    expect(writes).toHaveBeenCalledWith('');
  });
});
