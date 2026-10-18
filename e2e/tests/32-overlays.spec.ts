import { expect, test } from '../src/fixtures';

// R13 (docs/design/lean-scope.md 2.5): the dialog pops in, the phone dialog is a bottom sheet that slides in, and under reduced motion
// both only fade. Motion is read from the computed style (every duration, distance and scale is a token), because a frame sampled
// during a 200ms transition would make the test depend on the machine. The account dialog stands for every dialog: they share
// one component.

const NO_MOVE = ['none', 'matrix(1, 0, 0, 1, 0, 0)'];

/** Waits until the entrance transition has ended, so a measurement does not catch the dialog half way in. */
async function settled(dialog: import('@playwright/test').Locator): Promise<void> {
  await dialog.evaluate(element => Promise.all(element.getAnimations().map(animation => animation.finished)));
}

/** The tokens and the dialog's own transition, as the browser resolved them. */
async function readMotion(dialog: import('@playwright/test').Locator) {
  return dialog.evaluate(element => {
    const root = getComputedStyle(document.documentElement);
    const style = getComputedStyle(element);
    return {
      move: root.getPropertyValue('--ims-move-md').trim(),
      pop: root.getPropertyValue('--ims-scale-pop').trim(),
      slide: root.getPropertyValue('--ims-slide').trim(),
      properties: style.transitionProperty,
      durations: style.transitionDuration,
      opacity: style.opacity,
      transform: style.transform
    };
  });
}

test.describe('R13 Dialog on a 1280px desktop', () => {
  test.use({ viewport: { width: 1280, height: 800 } });

  test('given normal motion, then the dialog fades and pops over 200ms and 280ms, ends still and fully visible, and Escape returns the focus', async ({
    page,
    signIn,
    shell
  }) => {
    await signIn('staff');
    const dialog = await shell.openAccount();
    await settled(dialog);

    const motion = await readMotion(dialog);
    expect(motion.move).toBe('12px');
    expect(Number(motion.pop)).toBe(0.94);
    expect(motion.properties).toBe('opacity, transform');
    expect(motion.durations).toBe('0.2s, 0.28s');
    expect(NO_MOVE).toContain(motion.transform);

    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(shell.account).toBeFocused();
  });

  test('given reduced motion, then nothing lifts, grows or slides and the durations collapse', async ({
    page,
    signIn,
    shell
  }) => {
    await page.emulateMedia({ reducedMotion: 'reduce' });
    await signIn('staff');
    const dialog = await shell.openAccount();
    await settled(dialog);

    const motion = await readMotion(dialog);
    expect(motion.move).toBe('0px');
    expect(Number(motion.pop)).toBe(1);
    expect(motion.slide).toBe('0%');
    // The safety net of globals/_motion.scss makes every transition 0.01ms long.
    for (const seconds of motion.durations.split(',').map(parseFloat)) {
      expect(seconds).toBeLessThan(0.001);
    }
    expect(motion.opacity).toBe('1');
    expect(NO_MOVE).toContain(motion.transform);
  });

  for (const [scheme, alpha] of [
    ['light', '0.5'],
    ['dark', '0.6']
  ] as const) {
    test(`given the ${scheme} theme, then the scrim is the overlay colour at ${alpha}`, async ({
      page,
      signIn,
      shell
    }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await signIn('staff');
      const dialog = await shell.openAccount();

      const scrim = await dialog.evaluate(element => getComputedStyle(element, '::backdrop').backgroundColor);
      expect(scrim).toContain(`/ ${alpha})`);
    });
  }
});

for (const width of [360, 320]) {
  test.describe(`R13 Bottom sheet on a ${width}px phone`, () => {
    test.use({ viewport: { width, height: 700 }, hasTouch: true, isMobile: true });

    test('given a phone, then the dialog is a sheet on the bottom edge with 16px top corners that slides in, and its buttons stay inside', async ({
      page,
      signIn,
      shell
    }) => {
      await signIn('staff');
      const dialog = await shell.openAccount();
      await settled(dialog);

      const sheet = await dialog.evaluate(element => {
        const box = element.getBoundingClientRect();
        const style = getComputedStyle(element);
        const root = getComputedStyle(document.documentElement);
        return {
          left: box.left,
          right: box.right,
          bottom: box.bottom,
          window: { width: window.innerWidth, height: window.innerHeight },
          top: [style.borderTopLeftRadius, style.borderTopRightRadius],
          below: [style.borderBottomLeftRadius, style.borderBottomRightRadius],
          slide: root.getPropertyValue('--ims-slide').trim(),
          durations: style.transitionDuration,
          overflow: element.scrollWidth - element.clientWidth
        };
      });
      expect(sheet.left).toBeCloseTo(0, 0);
      expect(sheet.right).toBeCloseTo(sheet.window.width, 0);
      expect(sheet.bottom).toBeCloseTo(sheet.window.height, 0);
      expect(sheet.top).toEqual(['16px', '16px']);
      expect(sheet.below).toEqual(['0px', '0px']);
      expect(sheet.slide).toBe('100%');
      expect(sheet.durations).toBe('0.2s, 0.28s');
      expect(sheet.overflow).toBeLessThanOrEqual(0);

      const signOut = dialog.getByRole('button', { name: 'Sign out' });
      const box = await signOut.boundingBox();
      expect(box?.x ?? -1).toBeGreaterThanOrEqual(0);
      expect((box?.x ?? 0) + (box?.width ?? 0)).toBeLessThanOrEqual(width);
      await page.keyboard.press('Escape');
      await expect(dialog).toBeHidden();
    });
  });
}
