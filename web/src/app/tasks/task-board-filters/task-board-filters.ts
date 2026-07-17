import { Component, input, output } from '@angular/core';

import type { components } from '../../api/generated/pm-api';
import type { BoardQuery } from '../tasks-board.store';

type BoardFilterOption = components['schemas']['BoardOptionResponse'];

export interface BoardFilterChange {
  filter: 'state';
  value: string | null;
}

@Component({
  selector: 'form[pmTaskBoardFilters]',
  templateUrl: './task-board-filters.html',
  styleUrl: './task-board-filters.css',
  host: { '(submit)': '$event.preventDefault()' },
})
export class TaskBoardFilters {
  readonly states = input.required<BoardFilterOption[]>();
  readonly filters = input.required<BoardQuery>();
  readonly filterChange = output<BoardFilterChange>();
  readonly clearIntent = output<void>();

  protected change(value: string): void {
    this.filterChange.emit({ filter: 'state', value: value.trim() || null });
  }
}
