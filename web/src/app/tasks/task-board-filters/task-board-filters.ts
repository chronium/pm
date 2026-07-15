import { Component, input, output } from '@angular/core';

import type { components } from '../../api/generated/pm-api';
import type { BoardFilter, BoardQuery } from '../tasks-board.store';

type BoardFilterOption = components['schemas']['BoardOptionResponse'];

export interface BoardFilterChange {
  filter: BoardFilter;
  value: string | null;
}

@Component({
  selector: 'form[pmTaskBoardFilters]',
  templateUrl: './task-board-filters.html',
  styleUrl: './task-board-filters.css',
  host: { '(submit)': '$event.preventDefault()' },
})
export class TaskBoardFilters {
  readonly tracks = input.required<BoardFilterOption[]>();
  readonly milestones = input.required<BoardFilterOption[]>();
  readonly states = input.required<BoardFilterOption[]>();
  readonly filters = input.required<BoardQuery>();
  readonly filterChange = output<BoardFilterChange>();
  readonly clearIntent = output<void>();

  protected change(filter: BoardFilter, value: string): void {
    this.filterChange.emit({ filter, value: value.trim() || null });
  }
}
