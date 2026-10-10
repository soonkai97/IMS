import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { createServiceFactory, SpectatorService } from '@ngneat/spectator/jest';
import { I18nService, isMessageKey } from '@ims/core/i18n';
import { PageTitleService } from './page-title.service';
import { PageTitleStrategy } from './page-title.strategy';

@Component({ template: '' })
class BlankPage {}

describe('PageTitleService and PageTitleStrategy (EA-03)', () => {
  let spectator: SpectatorService<PageTitleService>;
  const createService = createServiceFactory({
    service: PageTitleService,
    providers: [
      provideRouter([
        { path: 'approvals', component: BlankPage, title: 'approvals.title' },
        { path: 'stock', component: BlankPage, title: 'stock.title' },
        { path: 'plain', component: BlankPage, title: 'not a key' }
      ]),
      { provide: TitleStrategy, useExisting: PageTitleStrategy }
    ]
  });
  const browserTitle = (): string => TestBed.inject(Title).getTitle();

  beforeEach(() => {
    spectator = createService();
  });

  it('every screen has its own title: "{screen} - {app}"', () => {
    spectator.service.setScreen('receipts.title', false);
    TestBed.flushEffects();
    expect(browserTitle()).toBe('Receipts - IMS');

    spectator.service.setScreen('stock.title', false);
    TestBed.flushEffects();
    expect(browserTitle()).toBe('Stock - IMS');
  });

  it('a page can add what it shows, such as the document number', () => {
    spectator.service.setScreen('receipts.title', false);
    spectator.service.setDetail('RCV-WH01-2610-0007');
    TestBed.flushEffects();

    expect(browserTitle()).toBe('RCV-WH01-2610-0007 - Receipts - IMS');
  });

  it('follows the language', async () => {
    spectator.service.setScreen('stock.title', false);
    await spectator.inject(I18nService).setLanguage('ms');
    TestBed.flushEffects();

    expect(browserTitle()).toBe('Stok - IMS');
    await spectator.inject(I18nService).setLanguage('en');
  });

  it('announces a route change, but not the first page load', () => {
    spectator.service.setScreen('home.title', false);
    expect(spectator.service.$announcement()).toBe('');

    spectator.service.setScreen('issues.title', true);
    expect(spectator.service.$announcement()).toBe('Issues - IMS');
  });

  it('every route change sets the tab title from the route and announces it; the first load is silent', async () => {
    const router = TestBed.inject(Router);

    await router.navigateByUrl('/approvals');
    TestBed.flushEffects();
    expect(browserTitle()).toBe('Approvals - IMS');
    expect(spectator.service.$announcement()).toBe('');

    await router.navigateByUrl('/stock');
    TestBed.flushEffects();
    expect(browserTitle()).toBe('Stock - IMS');
    expect(spectator.service.$announcement()).toBe('Stock - IMS');
  });

  it('a route title that is not a message key is ignored: the tab shows the app name', async () => {
    await TestBed.inject(Router).navigateByUrl('/plain');
    TestBed.flushEffects();

    expect(browserTitle()).toBe('IMS');
  });

  it('knows which strings are catalog keys', () => {
    expect(isMessageKey('receipts.title')).toBe(true);
    expect(isMessageKey('toString')).toBe(false);
    expect(isMessageKey(undefined)).toBe(false);
  });
});
