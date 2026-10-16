import { ConfirmService } from './confirm.service';

const REQUEST = { heading: 'Post?', confirmLabel: 'Post', cancelLabel: 'Back' };

describe('ConfirmService', () => {
  const service = new ConfirmService();

  it('ask emits the reason once when confirmed and completes', () => {
    const seen: string[] = [];
    let completed = false;
    service.ask(REQUEST).subscribe({
      next: reason => seen.push(reason),
      complete: () => (completed = true)
    });

    service.confirm('Wrong lot');

    expect(seen).toEqual(['Wrong lot']);
    expect(completed).toBe(true);
    expect(service.$pending()).toBeUndefined();
  });

  it('closing the dialog completes with no value', () => {
    const seen: unknown[] = [];
    let completed = false;
    service.choose(REQUEST).subscribe({
      next: value => seen.push(value),
      complete: () => (completed = true)
    });

    service.cancel();

    expect(seen).toEqual([]);
    expect(completed).toBe(true);
  });

  it('choose tells the alternative from a confirmation, and ask never emits for the alternative', () => {
    const chosen: unknown[] = [];
    const asked: string[] = [];
    service.choose(REQUEST).subscribe(value => chosen.push(value));
    service.alternate();
    service.ask(REQUEST).subscribe(reason => asked.push(reason));
    service.alternate();
    service.choose(REQUEST).subscribe(value => chosen.push(value));
    service.confirm('');

    expect(chosen).toEqual([{ choice: 'alternate' }, { choice: 'confirm', reason: '' }]);
    expect(asked).toEqual([]);
  });
});
