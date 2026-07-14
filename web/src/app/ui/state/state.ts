import { Component } from '@angular/core';

@Component({
  selector: 'pm-loading-state',
  template: '<div class="state" role="status" aria-live="polite"><span class="spinner" aria-hidden="true"></span><ng-content /></div>',
  styleUrl: './state.css',
})
export class PmLoadingState {}

@Component({
  selector: 'pm-empty-state',
  template: '<div class="state"><ng-content /></div>',
  styleUrl: './state.css',
})
export class PmEmptyState {}

@Component({
  selector: 'pm-error-state',
  template: '<div class="state state--error" role="alert"><ng-content /></div>',
  styleUrl: './state.css',
})
export class PmErrorState {}
