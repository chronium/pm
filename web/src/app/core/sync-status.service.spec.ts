import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { SyncStatusService, syncStatusInterceptor } from './sync-status.service';

describe('SyncStatusService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([syncStatusInterceptor])),
        provideHttpClientTesting(),
      ],
    }),
  );

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it('stays active until every tracked request completes', () => {
    const service = TestBed.inject(SyncStatusService);
    const first = service.begin();
    const second = service.begin();

    expect(service.syncing()).toBe(true);
    first();
    expect(service.syncing()).toBe(true);
    second();
    expect(service.syncing()).toBe(false);
  });
});
